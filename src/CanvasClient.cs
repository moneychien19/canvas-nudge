using System.Net;
using System.Net.Http.Headers;
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
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public CanvasClient(
        HttpClient http,
        string canvasBaseUrl,
        string token,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _http = http;
        _apiBase = canvasBaseUrl.TrimEnd('/') + "/api/v1";
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

    /// <summary>Published, peer-review-enabled assignments in a course (spec §4.3 step 2).</summary>
    public async Task<IReadOnlyList<CanvasAssignment>> GetPeerReviewAssignmentsAsync(
        long courseId, CancellationToken ct = default)
    {
        var assignments = await GetPaginatedAsync<CanvasAssignment>(
            $"/courses/{courseId}/assignments", ct);
        return assignments.Where(a => a.PeerReviews && a.Published).ToList();
    }

    /// <summary>Count this user's still-"assigned" reviews for one assignment (spec §4.3 step 3).</summary>
    public async Task<int> GetPendingPeerReviewCountAsync(
        long courseId, long assignmentId, long selfId, CancellationToken ct = default)
    {
        var reviews = await GetPaginatedAsync<CanvasPeerReview>(
            $"/courses/{courseId}/assignments/{assignmentId}/peer_reviews", ct);
        return reviews.Count(r => r.AssessorId == selfId && r.WorkflowState == "assigned");
    }

    /// <summary>
    /// Across the given courses, find assignments with pending peer reviews and
    /// group them (spec §4.3): one <see cref="PeerReviewGroup"/> per assignment
    /// that has at least one still-"assigned" review for this user.
    /// </summary>
    public async Task<IReadOnlyList<PeerReviewGroup>> GetPendingPeerReviewsAsync(
        IEnumerable<long> courseIds, long selfId, CancellationToken ct = default)
    {
        var groups = new List<PeerReviewGroup>();
        foreach (var courseId in courseIds)
        {
            foreach (var assignment in await GetPeerReviewAssignmentsAsync(courseId, ct))
            {
                var pending = await GetPendingPeerReviewCountAsync(courseId, assignment.Id, selfId, ct);
                if (pending > 0)
                    groups.Add(new PeerReviewGroup(courseId, assignment.Name, assignment.DueAt, pending));
            }
        }
        return groups;
    }

    /// <summary>GET a single JSON object and deserialize it.</summary>
    public async Task<T> GetAsync<T>(string path, CancellationToken ct = default)
    {
        var body = await SendWithRetryAsync(BuildUri(path), ct);
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
            var (body, nextLink) = await SendWithRetryWithLinkAsync(url, ct);
            var page = JsonSerializer.Deserialize<List<T>>(body, JsonOptions);
            if (page is not null) results.AddRange(page);
            url = nextLink;
        }

        return results;
    }

    private string BuildUri(string path, bool perPage = false)
    {
        var url = path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? path
            : $"{_apiBase}/{path.TrimStart('/')}";
        if (perPage && !url.Contains("per_page="))
            url += (url.Contains('?') ? "&" : "?") + "per_page=100";
        return url;
    }

    private async Task<string> SendWithRetryAsync(string url, CancellationToken ct)
        => (await SendWithRetryWithLinkAsync(url, ct)).Body;

    private async Task<(string Body, string? NextLink)> SendWithRetryWithLinkAsync(
        string url, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                return (body, ParseNextLink(response));
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new CanvasAuthException($"Canvas returned 401 Unauthorized for {url}.");

            var throttled = response.StatusCode == HttpStatusCode.Forbidden
                || (int)response.StatusCode == 429;
            if (throttled && attempt == 0)
            {
                await _delay(RetryDelay(response), ct);
                continue;
            }

            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new CanvasException((int)response.StatusCode,
                $"Canvas request failed: HTTP {(int)response.StatusCode} for {url}. Body: {detail}");
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
