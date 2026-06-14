namespace CanvasNudge;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Dev convenience: load a local .env if one exists. No-op in CI, where env
        // comes from Actions secrets/vars.
        DotNetEnv.Env.TraversePath().Load();

        var config = Config.Load(Environment.GetEnvironmentVariable);
        PrintConfig(config);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var canvas = new CanvasClient(http, config.CanvasBaseUrl, config.CanvasApiToken);

        // Phase 2 (CP-B): prove the Canvas read path — self id + outstanding
        // assignments. Course names (§4.4), peer reviews (§4.3), digest assembly
        // (Phase 4) and Discord delivery arrive next.
        var selfId = await canvas.GetSelfIdAsync();
        Console.WriteLine($"\nSelf id: {selfId}");

        var assignments = await canvas.GetTodoAssignmentsAsync();
        Console.WriteLine($"Outstanding assignments (to-do, type=submitting): {assignments.Count}");
        foreach (var item in assignments)
        {
            var a = item.Assignment!;
            var due = a.DueAt?.ToString("u") ?? "(no due date)";
            Console.WriteLine($"  • course {item.CourseId} | {a.Name} | due {due} | {a.HtmlUrl}");
        }

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
