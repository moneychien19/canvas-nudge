namespace CanvasNudge;

internal static class Program
{
    private static int Main(string[] args)
    {
        // Dev convenience: load a local .env if one exists. No-op in CI, where env
        // comes from Actions secrets/vars.
        DotNetEnv.Env.TraversePath().Load();

        var config = Config.Load(Environment.GetEnvironmentVariable);
        PrintConfig(config);
        return 0;
    }

    /// <summary>Dump resolved config for diagnostics — never print secret values.</summary>
    private static void PrintConfig(Config config)
    {
        Console.WriteLine("Resolved config:");
        Console.WriteLine($"  CANVAS_API_TOKEN          = {Redact(config.CanvasApiToken)}");
        Console.WriteLine($"  DISCORD_WEBHOOK_URL       = {Redact(config.DiscordWebhookUrl)}");
        Console.WriteLine($"  CANVAS_BASE_URL           = {config.CanvasBaseUrl}");
        Console.WriteLine($"  TIMEZONE                  = {config.Timezone}");
        Console.WriteLine($"  NOTIFY_WHEN_EMPTY         = {config.NotifyWhenEmpty}");
        Console.WriteLine($"  NOTIFY_ERRORS_TO_DISCORD  = {config.NotifyErrorsToDiscord}");
        Console.WriteLine($"  CANVAS_TOKEN_EXPIRES_AT   = {config.CanvasTokenExpiresAt ?? "(unset)"}");
    }

    private static string Redact(string secret) =>
        string.IsNullOrEmpty(secret) ? "(empty)" : $"(set, {secret.Length} chars)";
}
