namespace CanvasNudge;

/// <summary>
/// Strongly-typed runtime configuration, resolved from environment variables
/// (spec §3). Use <see cref="Load"/> with a lookup function so it is testable
/// without mutating real process env.
/// </summary>
internal sealed record Config(
    string CanvasApiToken,
    string DiscordWebhookUrl,
    string CanvasBaseUrl,
    string Timezone,
    bool NotifyWhenEmpty,
    bool NotifyErrorsToDiscord,
    string? CanvasTokenExpiresAt)
{
    public static Config Load(Func<string, string?> getEnv)
    {
        return new Config(
            CanvasApiToken: Required(getEnv, "CANVAS_API_TOKEN"),
            DiscordWebhookUrl: Required(getEnv, "DISCORD_WEBHOOK_URL"),
            CanvasBaseUrl: Optional(getEnv, "CANVAS_BASE_URL") ?? "https://canvas.cmu.edu",
            Timezone: Optional(getEnv, "TIMEZONE") ?? "America/New_York",
            NotifyWhenEmpty: Bool(getEnv, "NOTIFY_WHEN_EMPTY", true),
            NotifyErrorsToDiscord: Bool(getEnv, "NOTIFY_ERRORS_TO_DISCORD", true),
            CanvasTokenExpiresAt: Optional(getEnv, "CANVAS_TOKEN_EXPIRES_AT"));
    }

    private static string Required(Func<string, string?> getEnv, string key) =>
        Optional(getEnv, key)
        ?? throw new InvalidOperationException($"Missing required environment variable: {key}");

    /// <summary>Returns the trimmed value, or null if absent/blank.</summary>
    private static string? Optional(Func<string, string?> getEnv, string key)
    {
        var raw = getEnv(key);
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    private static bool Bool(Func<string, string?> getEnv, string key, bool fallback)
    {
        var v = Optional(getEnv, key);
        if (v is null) return fallback;
        return v.ToLowerInvariant() switch
        {
            "true" => true,
            "false" => false,
            _ => throw new InvalidOperationException(
                $"Invalid boolean for {key}: '{v}' (expected 'true' or 'false')."),
        };
    }
}
