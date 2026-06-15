using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CanvasNudge;

/// <summary>A non-2xx Canvas response that isn't a 401 (spec §4 fail-loud path).</summary>
internal class CanvasException : Exception
{
    public int StatusCode { get; }

    public CanvasException(int statusCode, string message) : base(message) => StatusCode = statusCode;
}

/// <summary>
/// A 401 from Canvas — almost always an expired/revoked PAT (spec §8.1).
/// Surfaced distinctly so the entry point can print the fix-it message.
/// </summary>
internal sealed class CanvasAuthException : CanvasException
{
    public CanvasAuthException(string message) : base(401, message) { }
}

/// <summary>
/// Canvas REST client: bearer auth, per_page=100, Link-header pagination, and a
/// single retry on 403/429 throttling (spec §4). 401 fails loud immediately.
/// </summary>
internal sealed class CanvasClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Canvas uses snake_case; map to PascalCase DTO properties.
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly TimeSpan DefaultThrottleDelay = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly string _apiBase;
    private readonly string _graphqlUrl;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public CanvasClient(
        HttpClient http,
        string canvasBaseUrl,
        string token,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _http = http;
        var root = canvasBaseUrl.TrimEnd('/');
        _apiBase = root + "/api/v1";
        _graphqlUrl = root + "/api/graphql";
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _delay = delay ?? Task.Delay;
    }

    /// <summary>Resolve the authenticated user's id (spec §4.1).</summary>
    public async Task<long> GetSelfIdAsync(CancellationToken ct = default) =>
        (await GetAsync<CanvasUser>("/users/self", ct)).Id;

    /// <summary>
    /// Outstanding assignments via the to-do list (spec §4.2): keep only
    /// type=="submitting" items that carry an assignment. Canvas has already
    /// filtered to not-yet-done, so no completion check is needed here.
    /// </summary>
    public async Task<IReadOnlyList<CanvasTodoItem>> GetTodoAssignmentsAsync(CancellationToken ct = default)
    {
        var todo = await GetPaginatedAsync<CanvasTodoItem>("/users/self/todo", ct);
        return todo.Where(t => t.Type == "submitting" && t.Assignment is not null).ToList();
    }

    /// <summary>List the user's active courses (spec §4.4 / §4.3 step 1).</summary>
    public Task<List<CanvasCourse>> GetActiveCoursesAsync(CancellationToken ct = default) =>
        GetPaginatedAsync<CanvasCourse>("/courses?enrollment_state=active", ct);

    // GraphQL query: the peer reviews THIS user must complete, per assignment in a
    // course. The REST peer_reviews index can't do this for a student — it only
    // returns reviews of the student's OWN submission (spec §4.3). GraphQL's
    // assessmentRequestsForCurrentUser correctly scopes to "reviews I owe".
    private const string PeerReviewQuery = """
        query($courseId: ID!) {
          course(id: $courseId) {
            assignmentsConnection {
              nodes {
                _id
                name
                dueAt
                peerReviews { dueAt }
                assessmentRequestsForCurrentUser { workflowState }
              }
            }
          }
        }
        """;

    /// <summary>
    /// Across the given courses, find assignments with peer reviews this user still
    /// owes (workflowState=="assigned"), grouped per assignment (spec §4.3, via
    /// GraphQL). One <see cref="PeerReviewGroup"/> per assignment with ≥1 pending.
    /// </summary>
    public async Task<IReadOnlyList<PeerReviewGroup>> GetPendingPeerReviewsAsync(
        IEnumerable<long> courseIds, CancellationToken ct = default)
    {
        var groups = new List<PeerReviewGroup>();
        foreach (var courseId in courseIds)
        {
            var data = await PostGraphQlAsync(PeerReviewQuery, new { courseId = courseId.ToString() }, ct);
            groups.AddRange(PeerReviewParser.FromCourseData(data, courseId));
        }
        return groups;
    }

    /// <summary>POST a GraphQL query and return its "data" element; throws on GraphQL errors.</summary>
    public async Task<JsonElement> PostGraphQlAsync(
        string query, object? variables, CancellationToken ct = default)
    {
        var (body, _) = await SendWithRetryAsync(() =>
            new HttpRequestMessage(HttpMethod.Post, _graphqlUrl)
            {
                Content = JsonContent.Create(new { query, variables }),
            }, ct);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array
            && errors.GetArrayLength() > 0)
        {
            throw new CanvasException(0, $"Canvas GraphQL error: {errors.GetRawText()}");
        }
        // Clone so the value survives disposal of the JsonDocument.
        return root.GetProperty("data").Clone();
    }

    /// <summary>GET a single JSON object and deserialize it.</summary>
    public async Task<T> GetAsync<T>(string path, CancellationToken ct = default)
    {
        var (body, _) = await SendWithRetryAsync(() => Get(BuildUri(path)), ct);
        return JsonSerializer.Deserialize<T>(body, JsonOptions)
            ?? throw new CanvasException(0, $"Canvas returned null for {path}.");
    }

    /// <summary>GET a paginated JSON array, following Link rel="next" until exhausted.</summary>
    public async Task<List<T>> GetPaginatedAsync<T>(string path, CancellationToken ct = default)
    {
        var results = new List<T>();
        string? url = BuildUri(path, perPage: true);

        while (url is not null)
        {
            var nextUrl = url;
            var (body, nextLink) = await SendWithRetryAsync(() => Get(nextUrl), ct);
            var page = JsonSerializer.Deserialize<List<T>>(body, JsonOptions);
            if (page is not null) results.AddRange(page);
            url = nextLink;
        }

        return results;
    }

    private static HttpRequestMessage Get(string url) => new(HttpMethod.Get, url);

    private string BuildUri(string path, bool perPage = false)
    {
        var url = path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? path
            : $"{_apiBase}/{path.TrimStart('/')}";
        if (perPage && !url.Contains("per_page="))
            url += (url.Contains('?') ? "&" : "?") + "per_page=100";
        return url;
    }

    private async Task<(string Body, string? NextLink)> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = requestFactory();
            var target = request.RequestUri;
            using var response = await _http.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                return (body, ParseNextLink(response));
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new CanvasAuthException($"Canvas returned 401 Unauthorized for {target}.");

            var throttled = response.StatusCode == HttpStatusCode.Forbidden
                || (int)response.StatusCode == 429;
            if (throttled && attempt == 0)
            {
                await _delay(RetryDelay(response), ct);
                continue;
            }

            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new CanvasException((int)response.StatusCode,
                $"Canvas request failed: HTTP {(int)response.StatusCode} for {target}. Body: {detail}");
        }
    }

    private static TimeSpan RetryDelay(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Retry-After", out var values)
            && int.TryParse(values.FirstOrDefault(), out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }
        return DefaultThrottleDelay;
    }

    /// <summary>Extract the rel="next" URL from the Link response header, if any.</summary>
    private static string? ParseNextLink(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out var values)) return null;

        foreach (var segment in string.Join(",", values).Split(','))
        {
            var parts = segment.Split(';');
            if (parts.Length < 2) continue;
            var isNext = parts.Skip(1).Any(p =>
                p.Trim().Replace("\"", "").Equals("rel=next", StringComparison.OrdinalIgnoreCase));
            if (!isNext) continue;

            var raw = parts[0].Trim();
            if (raw.StartsWith('<') && raw.EndsWith('>'))
                return raw[1..^1];
        }
        return null;
    }
}
