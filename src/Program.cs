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

        // Phase 3 (CP-B): full Canvas read path — self id, course map, outstanding
        // assignments, and pending peer reviews. Digest assembly (Phase 4) and
        // Discord delivery arrive next.
        var selfId = await canvas.GetSelfIdAsync();
        Console.WriteLine($"\nSelf id: {selfId}");

        var courses = await canvas.GetActiveCoursesAsync();
        var courseLabels = CanvasCourse.BuildLabelMap(courses);
        Console.WriteLine($"Active courses: {courses.Count}");

        string Label(long courseId) =>
            courseLabels.TryGetValue(courseId, out var name) ? name : $"course {courseId}";

        var assignments = await canvas.GetTodoAssignmentsAsync();
        Console.WriteLine($"\nOutstanding assignments (to-do, type=submitting): {assignments.Count}");
        foreach (var item in assignments)
        {
            var a = item.Assignment!;
            var due = a.DueAt?.ToString("u") ?? "(no due date)";
            Console.WriteLine($"  • [{Label(item.CourseId)}] {a.Name} | due {due} | {a.HtmlUrl}");
        }

        var peerReviews = await canvas.GetPendingPeerReviewsAsync(courseLabels.Keys, selfId);
        Console.WriteLine($"\nPending peer reviews (grouped by assignment): {peerReviews.Count}");
        foreach (var g in peerReviews)
        {
            var due = g.DueAt?.ToString("u") ?? "(no due date)";
            Console.WriteLine($"  • [{Label(g.CourseId)}] {g.AssignmentName} | pending {g.PendingCount} | due {due}");
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
