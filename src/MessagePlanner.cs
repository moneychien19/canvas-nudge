namespace CanvasNudge;

/// <summary>
/// Decides what (if anything) to POST to Discord given the assembled digest, the
/// empty-state preference, and any active PAT-expiry warning (spec §5 + §8.2).
/// </summary>
internal static class MessagePlanner
{
    /// <summary>Celebratory empty-state line (spec §5). Also carries the warning when set.</summary>
    public static string EmptyMessage(Language lang) =>
        lang.Pick("All caught up — nothing outstanding 🎉", "恭喜，目前沒有未完成事項 🎉");

    /// <summary>
    /// Returns the payload to send, or null to stay silent. An active
    /// <paramref name="expiryWarning"/> is prepended and forces a send even on an
    /// empty list, so the warning is never dropped (spec §8.2 gap fix).
    /// </summary>
    public static DiscordWebhookPayload? Plan(
        DigestContent content, bool notifyWhenEmpty, string? expiryWarning, Language lang)
    {
        if (content.TotalCount > 0)
        {
            var description = expiryWarning is null
                ? content.Description
                : $"{expiryWarning}\n\n{content.Description}";
            return new DiscordWebhookPayload(
                Embeds: new[] { new DiscordEmbed(Title: content.Header, Description: description) });
        }

        // Empty list: send only if the user wants empty pings, or a warning is active.
        if (!notifyWhenEmpty && expiryWarning is null) return null;

        var body = expiryWarning is null
            ? EmptyMessage(lang)
            : $"{expiryWarning}\n{EmptyMessage(lang)}";
        return new DiscordWebhookPayload(Content: body);
    }
}
