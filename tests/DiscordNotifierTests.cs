using System.Net;
using System.Text;
using CanvasNudge;
using Xunit;

namespace CanvasNudge.Tests;

public class DiscordNotifierTests
{
    /// <summary>Captures the outgoing request and returns a canned response.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _responseBody;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        public StubHandler(HttpStatusCode status, string responseBody = "")
        {
            _status = status;
            _responseBody = responseBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8),
            };
        }
    }

    /// <summary>Replays a queued sequence of responses.</summary>
    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;
        public int Calls { get; private set; }
        public SequenceHandler(params HttpResponseMessage[] responses) =>
            _responses = new Queue<HttpResponseMessage>(responses);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private static HttpResponseMessage Resp(int status, string? retryAfter = null)
    {
        var msg = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("") };
        if (retryAfter is not null) msg.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        return msg;
    }

    private static (Func<TimeSpan, CancellationToken, Task> fn, List<TimeSpan> calls) FakeDelay()
    {
        var calls = new List<TimeSpan>();
        return ((d, _) => { calls.Add(d); return Task.CompletedTask; }, calls);
    }

    private const string Webhook = "https://discord.com/api/webhooks/1/abc";

    [Fact]
    public async Task SendAsync_RetriesOnce_On429_ThenSucceeds()
    {
        var handler = new SequenceHandler(Resp(429), Resp(204));
        var (delay, calls) = FakeDelay();
        var notifier = new DiscordNotifier(new HttpClient(handler), Webhook, delay);

        await notifier.SendAsync(new DiscordWebhookPayload(Content: "x"));

        Assert.Equal(2, handler.Calls);
        Assert.Single(calls);
    }

    [Fact]
    public async Task SendAsync_HonorsRetryAfter_On429()
    {
        var handler = new SequenceHandler(Resp(429, retryAfter: "4"), Resp(204));
        var (delay, calls) = FakeDelay();
        var notifier = new DiscordNotifier(new HttpClient(handler), Webhook, delay);

        await notifier.SendAsync(new DiscordWebhookPayload(Content: "x"));

        Assert.Equal(TimeSpan.FromSeconds(4), calls[0]);
    }

    [Fact]
    public async Task SendAsync_Throws_When429Persists()
    {
        var handler = new SequenceHandler(Resp(429), Resp(429));
        var (delay, _) = FakeDelay();
        var notifier = new DiscordNotifier(new HttpClient(handler), Webhook, delay);

        await Assert.ThrowsAsync<DiscordException>(
            () => notifier.SendAsync(new DiscordWebhookPayload(Content: "x")));
        Assert.Equal(2, handler.Calls);  // original + 1 retry
    }

    [Fact]
    public async Task SendAsync_PostsJsonToWebhook_OnSuccess()
    {
        var stub = new StubHandler(HttpStatusCode.NoContent);
        var notifier = new DiscordNotifier(new HttpClient(stub), Webhook);

        await notifier.SendAsync(new DiscordWebhookPayload(Content: "hello 待完成"));

        Assert.NotNull(stub.LastRequest);
        Assert.Equal(HttpMethod.Post, stub.LastRequest!.Method);
        Assert.Equal(Webhook, stub.LastRequest.RequestUri!.ToString());
        Assert.Contains("application/json", stub.LastRequest.Content!.Headers.ContentType!.ToString());
        Assert.Contains("hello 待完成", stub.LastBody);
    }

    [Fact]
    public async Task SendAsync_SerializesEmbeds()
    {
        var stub = new StubHandler(HttpStatusCode.NoContent);
        var notifier = new DiscordNotifier(new HttpClient(stub), Webhook);

        await notifier.SendAsync(new DiscordWebhookPayload(
            Embeds: new[] { new DiscordEmbed(Title: "待完成", Description: "• item") }));

        Assert.Contains("\"embeds\"", stub.LastBody);
        // CJK must be written literally (readable on the wire), not as \uXXXX.
        Assert.Contains("待完成", stub.LastBody);
        Assert.Contains("• item", stub.LastBody);
    }

    [Fact]
    public async Task SendAsync_Throws_WithStatusAndBody_OnNon2xx()
    {
        var stub = new StubHandler(HttpStatusCode.BadRequest, "{\"message\":\"bad webhook\"}");
        var notifier = new DiscordNotifier(new HttpClient(stub), Webhook);

        var ex = await Assert.ThrowsAsync<DiscordException>(
            () => notifier.SendAsync(new DiscordWebhookPayload(Content: "x")));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("bad webhook", ex.ResponseBody);
        Assert.Contains("400", ex.Message);
    }
}
