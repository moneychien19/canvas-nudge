using System.Text.Json.Serialization;

namespace CanvasNudge;

// --- Canvas API DTOs. Property names map from snake_case via the client's
//     JsonNamingPolicy.SnakeCaseLower (see CanvasClient), so no attributes needed. ---

internal sealed record CanvasUser(long Id);

/// <summary>An item from GET /users/self/todo (spec §4.2).</summary>
internal sealed record CanvasTodoItem(string Type, long CourseId, CanvasAssignment? Assignment);

/// <summary>
/// A Canvas assignment. Reused by the todo list (§4.2) and the peer-review
/// scan (§4.3). PeerReviews/Published are only populated by the assignments
/// endpoint; they default false elsewhere.
/// </summary>
internal sealed record CanvasAssignment(
    long Id,
    string Name,
    DateTimeOffset? DueAt,
    string? HtmlUrl,
    bool PeerReviews = false,
    bool Published = false);

// --- Discord webhook payload DTOs (System.Text.Json, explicit names). ---

internal sealed record DiscordWebhookPayload(
    [property: JsonPropertyName("content")] string? Content = null,
    [property: JsonPropertyName("embeds")] IReadOnlyList<DiscordEmbed>? Embeds = null);

internal sealed record DiscordEmbed(
    [property: JsonPropertyName("title")] string? Title = null,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("color")] int? Color = null);
