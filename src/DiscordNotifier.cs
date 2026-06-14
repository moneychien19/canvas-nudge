using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CanvasNudge;

/// <summary>Raised when the Discord webhook returns a non-2xx response.</summary>
internal sealed class DiscordException : Exception
{
    public int StatusCode { get; }
    public string ResponseBody { get; }

    public DiscordException(int statusCode, string responseBody)
        : base($"Discord webhook POST failed: HTTP {statusCode}. Body: {responseBody}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}

/// <summary>Posts digest messages to a Discord incoming webhook (spec §5).</summary>
internal sealed class DiscordNotifier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Omit null fields so we don't send empty content/embeds.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Write CJK/emoji literally instead of \uXXXX — readable on the wire.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly HttpClient _http;
    private readonly string _webhookUrl;

    public DiscordNotifier(HttpClient http, string webhookUrl)
    {
        _http = http;
        _webhookUrl = webhookUrl;
    }

    public async Task SendAsync(DiscordWebhookPayload payload, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync(_webhookUrl, payload, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new DiscordException((int)response.StatusCode, body);
        }
    }
}
