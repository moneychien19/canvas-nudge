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

    private const string Webhook = "https://discord.com/api/webhooks/1/abc";

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
