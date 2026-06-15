using System.Text.Json;

namespace CanvasNudge;

/// <summary>
/// Parses the GraphQL "data" element of the peer-review query (spec §4.3) into
/// <see cref="PeerReviewGroup"/> rows — one per assignment with ≥1 review this
/// user still owes (workflowState=="assigned"). Pure; GraphQL field names are
/// camelCase and read verbatim.
/// </summary>
internal static class PeerReviewParser
{
    public static IEnumerable<PeerReviewGroup> FromCourseData(JsonElement data, long courseId)
    {
        if (!TryProp(data, "course", out var course) || course.ValueKind != JsonValueKind.Object)
            yield break;
        if (!TryProp(course, "assignmentsConnection", out var conn))
            yield break;
        if (!TryProp(conn, "nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var node in nodes.EnumerateArray())
        {
            if (!TryProp(node, "assessmentRequestsForCurrentUser", out var reqs)
                || reqs.ValueKind != JsonValueKind.Array)
                continue;

            var pending = reqs.EnumerateArray().Count(r =>
                TryProp(r, "workflowState", out var ws) && ws.GetString() == "assigned");
            if (pending == 0) continue;

            var name = TryProp(node, "name", out var n) ? n.GetString() ?? "(unnamed)" : "(unnamed)";
            DateTimeOffset? dueAt =
                TryProp(node, "dueAt", out var d) && d.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(d.GetString(), out var parsed)
                    ? parsed
                    : null;

            yield return new PeerReviewGroup(courseId, name, dueAt, pending);
        }
    }

    private static bool TryProp(JsonElement el, string name, out JsonElement value)
    {
        if (el.ValueKind == JsonValueKind.Object)
            return el.TryGetProperty(name, out value);
        value = default;
        return false;
    }
}
