using System.Text.Json.Serialization;

namespace CanvasNudge;

// --- Discord webhook payload DTOs (System.Text.Json). Canvas DTOs added later. ---

internal sealed record DiscordWebhookPayload(
    [property: JsonPropertyName("content")] string? Content = null,
    [property: JsonPropertyName("embeds")] IReadOnlyList<DiscordEmbed>? Embeds = null);

internal sealed record DiscordEmbed(
    [property: JsonPropertyName("title")] string? Title = null,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("color")] int? Color = null);
