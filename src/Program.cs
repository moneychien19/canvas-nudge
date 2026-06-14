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

        // Fetch the current outstanding set (spec §4).
        var selfId = await canvas.GetSelfIdAsync();
        var courses = await canvas.GetActiveCoursesAsync();
        var courseLabels = CanvasCourse.BuildLabelMap(courses);
        string Label(long courseId) =>
            courseLabels.TryGetValue(courseId, out var name) ? name : $"course {courseId}";

        var todo = await canvas.GetTodoAssignmentsAsync();
        var peerGroups = await canvas.GetPendingPeerReviewsAsync(courseLabels.Keys, selfId);

        var assignments = todo
            .Select(t => new OutstandingAssignment(
                Label(t.CourseId), t.Assignment!.Name, t.Assignment.DueAt, t.Assignment.HtmlUrl))
            .ToList();
        var peerReviews = peerGroups
            .Select(g => new OutstandingPeerReview(
                Label(g.CourseId), g.AssignmentName, g.DueAt, g.PendingCount))
            .ToList();

        // Assemble the digest and decide what to send (spec §5). Expiry-warning
        // (§8.2) is wired in Phase 6.
        var tz = TimeZoneInfo.FindSystemTimeZoneById(config.Timezone);
        var content = DigestBuilder.Build(assignments, peerReviews, DateTimeOffset.UtcNow, tz);
        Console.WriteLine($"\nOutstanding items: {content.TotalCount}");

        var payload = MessagePlanner.Plan(content, config.NotifyWhenEmpty, expiryWarning: null);
        var notifier = new DiscordNotifier(http, config.DiscordWebhookUrl);
        if (payload is not null)
        {
            await notifier.SendAsync(payload);
            Console.WriteLine("Message sent to Discord.");
        }
        else
        {
            Console.WriteLine("Nothing outstanding; staying silent (NOTIFY_WHEN_EMPTY=false).");
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
