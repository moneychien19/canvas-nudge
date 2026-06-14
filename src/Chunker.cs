using System.Text;

namespace CanvasNudge;

/// <summary>
/// Defensive splitter for over-length embed descriptions (spec §5). Splits at
/// line (bullet) boundaries into multiple sequential payloads — never cuts a
/// single item, never truncates. This is a safety net, not the expected path.
/// </summary>
internal static class Chunker
{
    public static IReadOnlyList<DiscordWebhookPayload> Chunk(
        DiscordWebhookPayload payload, int maxDescriptionLength = 4096)
    {
        var embed = payload.Embeds?.Count == 1 ? payload.Embeds[0] : null;
        var description = embed?.Description;
        if (embed is null || description is null || description.Length <= maxDescriptionLength)
            return new[] { payload };

        var chunks = SplitAtLineBoundaries(description, maxDescriptionLength);
        var result = new List<DiscordWebhookPayload>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            // Only the first chunk keeps the title; continuations omit it.
            var title = i == 0 ? embed.Title : null;
            result.Add(new DiscordWebhookPayload(
                Embeds: new[] { embed with { Title = title, Description = chunks[i] } }));
        }
        return result;
    }

    private static List<string> SplitAtLineBoundaries(string description, int maxLength)
    {
        var chunks = new List<string>();
        var current = new StringBuilder();

        foreach (var line in description.Split('\n'))
        {
            var addition = current.Length == 0 ? line.Length : line.Length + 1; // +1 for '\n'
            if (current.Length > 0 && current.Length + addition > maxLength)
            {
                chunks.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0) current.Append('\n');
            current.Append(line); // an oversize single line is emitted whole (never truncated)
        }

        if (current.Length > 0) chunks.Add(current.ToString());
        return chunks;
    }
}
