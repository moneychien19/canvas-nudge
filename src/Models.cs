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

/// <summary>A course from GET /courses (spec §4.4).</summary>
internal sealed record CanvasCourse(long Id, string? Name, string? CourseCode)
{
    /// <summary>Display label: prefer name, fall back to course_code, then id.</summary>
    public string Label => Name ?? CourseCode ?? $"course {Id}";

    public static IReadOnlyDictionary<long, string> BuildLabelMap(IEnumerable<CanvasCourse> courses) =>
        courses.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().Label);
}

/// <summary>A single peer-review row (spec §4.3).</summary>
internal sealed record CanvasPeerReview(long AssessorId, string WorkflowState);

/// <summary>
/// Pending peer reviews grouped by their parent assignment (spec §4.3): one row
/// per assignment carrying the count N of still-"assigned" reviews.
/// </summary>
internal sealed record PeerReviewGroup(
    long CourseId,
    string AssignmentName,
    DateTimeOffset? DueAt,
    int PendingCount);

// --- Discord webhook payload DTOs (System.Text.Json, explicit names). ---

internal sealed record DiscordWebhookPayload(
    [property: JsonPropertyName("content")] string? Content = null,
    [property: JsonPropertyName("embeds")] IReadOnlyList<DiscordEmbed>? Embeds = null);

internal sealed record DiscordEmbed(
    [property: JsonPropertyName("title")] string? Title = null,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("color")] int? Color = null);
