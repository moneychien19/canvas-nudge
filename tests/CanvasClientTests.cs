using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CanvasNudge;
using Xunit;

namespace CanvasNudge.Tests;

public class CanvasClientTests
{
    /// <summary>Replays a queued sequence of responses, recording each request's URI + auth.</summary>
    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;
        public List<string> RequestUris { get; } = new();
        public List<string?> AuthHeaders { get; } = new();

        public SequenceHandler(params HttpResponseMessage[] responses) =>
            _responses = new Queue<HttpResponseMessage>(responses);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!.ToString());
            AuthHeaders.Add(request.Headers.Authorization?.ToString());
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body, string? linkHeader = null)
    {
        var msg = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (linkHeader is not null) msg.Headers.TryAddWithoutValidation("Link", linkHeader);
        return msg;
    }

    private const string Base = "https://canvas.example.edu";

    // A recording no-op delay so retry tests don't actually sleep.
    private static (Func<TimeSpan, CancellationToken, Task> fn, List<TimeSpan> calls) FakeDelay()
    {
        var calls = new List<TimeSpan>();
        return ((d, _) => { calls.Add(d); return Task.CompletedTask; }, calls);
    }

    private sealed record Item(int Id);

    [Fact]
    public async Task GetAsync_SendsBearerToken_AndHitsApiV1()
    {
        var handler = new SequenceHandler(Json(HttpStatusCode.OK, "{\"id\":42}"));
        var (delay, _) = FakeDelay();
        var client = new CanvasClient(new HttpClient(handler), Base, "secret-tok", delay);

        var result = await client.GetAsync<Item>("/users/self");

        Assert.Equal(42, result.Id);
        Assert.Equal("Bearer secret-tok", handler.AuthHeaders[0]);
        Assert.StartsWith($"{Base}/api/v1/users/self", handler.RequestUris[0]);
    }

    [Fact]
    public async Task GetPaginated_FollowsLinkNext_AndConcatenates()
    {
        var page1 = Json(HttpStatusCode.OK, "[{\"id\":1},{\"id\":2}]",
            linkHeader: $"<{Base}/api/v1/courses?page=2&per_page=100>; rel=\"next\", " +
                        $"<{Base}/api/v1/courses?page=2&per_page=100>; rel=\"last\"");
        var page2 = Json(HttpStatusCode.OK, "[{\"id\":3}]"); // no next link → stop
        var handler = new SequenceHandler(page1, page2);
        var (delay, _) = FakeDelay();
        var client = new CanvasClient(new HttpClient(handler), Base, "tok", delay);

        var items = await client.GetPaginatedAsync<Item>("/courses");

        Assert.Equal(new[] { 1, 2, 3 }, items.Select(i => i.Id).ToArray());
        Assert.Equal(2, handler.RequestUris.Count);
        Assert.Contains("per_page=100", handler.RequestUris[0]);
    }

    [Fact]
    public async Task Get_Throws_CanvasAuthException_On401_NoRetry()
    {
        var handler = new SequenceHandler(Json(HttpStatusCode.Unauthorized, "unauthorized"));
        var (delay, calls) = FakeDelay();
        var client = new CanvasClient(new HttpClient(handler), Base, "tok", delay);

        await Assert.ThrowsAsync<CanvasAuthException>(() => client.GetAsync<Item>("/users/self"));
        Assert.Single(handler.RequestUris);  // no retry on 401
        Assert.Empty(calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData((HttpStatusCode)429)]
    public async Task Get_RetriesOnce_ThenSucceeds_OnThrottle(HttpStatusCode throttle)
    {
        var handler = new SequenceHandler(
            Json(throttle, "rate limited"),
            Json(HttpStatusCode.OK, "{\"id\":7}"));
        var (delay, calls) = FakeDelay();
        var client = new CanvasClient(new HttpClient(handler), Base, "tok", delay);

        var result = await client.GetAsync<Item>("/users/self");

        Assert.Equal(7, result.Id);
        Assert.Equal(2, handler.RequestUris.Count);
        Assert.Single(calls);  // slept exactly once
    }

    [Fact]
    public async Task Get_FailsLoud_AfterRetryStillThrottled()
    {
        var handler = new SequenceHandler(
            Json((HttpStatusCode)429, "rate limited"),
            Json((HttpStatusCode)429, "rate limited again"));
        var (delay, calls) = FakeDelay();
        var client = new CanvasClient(new HttpClient(handler), Base, "tok", delay);

        await Assert.ThrowsAsync<CanvasException>(() => client.GetAsync<Item>("/users/self"));
        Assert.Equal(2, handler.RequestUris.Count);  // original + 1 retry, then give up
        Assert.Single(calls);
    }

    [Fact]
    public async Task Get_HonorsRetryAfterHeader()
    {
        var throttled = Json(HttpStatusCode.Forbidden, "slow down");
        throttled.Headers.TryAddWithoutValidation("Retry-After", "9");
        var handler = new SequenceHandler(throttled, Json(HttpStatusCode.OK, "{\"id\":1}"));
        var (delay, calls) = FakeDelay();
        var client = new CanvasClient(new HttpClient(handler), Base, "tok", delay);

        await client.GetAsync<Item>("/users/self");

        Assert.Equal(TimeSpan.FromSeconds(9), calls[0]);
    }
}
