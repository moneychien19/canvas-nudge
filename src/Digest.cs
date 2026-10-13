using System.Globalization;
using System.Text;

namespace CanvasNudge;

/// <summary>
/// Date/time formatting for the digest (spec §5), in English or Traditional
/// Chinese. All computation is done in the target timezone; weekdays use fixed
/// tables so output doesn't depend on ICU culture data.
/// </summary>
internal static class TimeFormatting
{
    private static readonly string[] WeekdaysEn = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
    private static readonly string[] WeekdaysZhHant = { "週日", "週一", "週二", "週三", "週四", "週五", "週六" };

    public static string Weekday(DayOfWeek day, Language lang) =>
        (lang == Language.En ? WeekdaysEn : WeekdaysZhHant)[(int)day];

    /// <summary>
    /// Absolute due in the target timezone: "Tue 6/17 23:59" (en) or
    /// "6/17（週二）23:59" (zh-Hant).
    /// </summary>
    public static string AbsoluteDue(DateTimeOffset dueUtc, TimeZoneInfo tz, Language lang)
    {
        var local = TimeZoneInfo.ConvertTime(dueUtc, tz);
        var weekday = Weekday(local.DayOfWeek, lang);
        return lang.Pick(
            $"{weekday} {local.Month}/{local.Day} {local:HH:mm}",
            $"{local.Month}/{local.Day}（{weekday}）{local:HH:mm}");
    }

    /// <summary>Short due like "6/14" (used for peer-review context).</summary>
    public static string ShortDate(DateTimeOffset dueUtc, TimeZoneInfo tz)
    {
        var local = TimeZoneInfo.ConvertTime(dueUtc, tz);
        return $"{local.Month}/{local.Day}";
    }

    /// <summary>
    /// Relative hint (spec §5): in N days / in N hours / due today / overdue by N
    /// days (zh-Hant: 還有 N 天 / 還有 N 小時 / 今天截止 / 已逾期 N 天). Day counts
    /// use calendar-day differences in the target tz, so "in 2 days" matches the
    /// spec example. Same-day overdue (passed earlier today) renders as a bare
    /// "overdue".
    /// </summary>
    public static string RelativeHint(
        DateTimeOffset dueUtc, DateTimeOffset nowUtc, TimeZoneInfo tz, Language lang, out bool overdue)
    {
        var dueLocal = TimeZoneInfo.ConvertTime(dueUtc, tz);
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, tz);
        var delta = dueUtc - nowUtc;

        if (delta.Ticks < 0)
        {
            overdue = true;
            var overdueDays = (nowLocal.Date - dueLocal.Date).Days;
            return overdueDays >= 1
                ? lang.Pick($"overdue by {LanguageExtensions.Plural(overdueDays, "day")}", $"已逾期 {overdueDays} 天")
                : lang.Pick("overdue", "已逾期");
        }

        overdue = false;
        var dayDiff = (dueLocal.Date - nowLocal.Date).Days;
        if (dayDiff >= 1)
            return lang.Pick($"in {LanguageExtensions.Plural(dayDiff, "day")}", $"還有 {dayDiff} 天");

        var hours = (int)Math.Floor(delta.TotalHours);
        return hours >= 1
            ? lang.Pick($"in {LanguageExtensions.Plural(hours, "hour")}", $"還有 {hours} 小時")
            : lang.Pick("due today", "今天截止");
    }
}

/// <summary>
/// Builds the digest message body (spec §5): header + category groups, each
/// sorted by due date ascending with undated items last. Template text is
/// localized; Canvas-supplied names are emitted verbatim.
/// </summary>
internal static class DigestBuilder
{
    public static DigestContent Build(
        IReadOnlyCollection<OutstandingAssignment> assignments,
        IReadOnlyCollection<OutstandingPeerReview> peerReviews,
        DateTimeOffset nowUtc,
        TimeZoneInfo tz,
        Language lang)
    {
        var total = assignments.Count + peerReviews.Count;
        var header = BuildHeader(total, nowUtc, tz, lang);

        var body = new StringBuilder();

        if (assignments.Count > 0)
        {
            body.Append(lang.Pick("Assignments", "作業")).Append(" (").Append(assignments.Count).Append(")\n");
            foreach (var a in SortByDue(assignments, x => x.DueAt))
                body.Append(AssignmentLine(a, nowUtc, tz, lang)).Append('\n');
        }

        if (peerReviews.Count > 0)
        {
            if (body.Length > 0) body.Append('\n');
            body.Append(lang.Pick("Peer Reviews", "同儕互評")).Append(" (").Append(peerReviews.Count).Append(")\n");
            foreach (var p in SortByDue(peerReviews, x => x.DueAt))
                body.Append(PeerReviewLine(p, nowUtc, tz, lang)).Append('\n');
        }

        return new DigestContent(header, body.ToString().TrimEnd('\n'), total);
    }

    /// <summary>Wrap the digest as a single Discord embed (header as title, body as description).</summary>
    public static DiscordWebhookPayload ToPayload(DigestContent content) =>
        new(Embeds: new[] { new DiscordEmbed(Title: content.Header, Description: content.Description) });

    private static string BuildHeader(int total, DateTimeOffset nowUtc, TimeZoneInfo tz, Language lang)
    {
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, tz);
        var stamp = nowLocal.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
        return lang.Pick(
            $"📋 Outstanding — {LanguageExtensions.Plural(total, "item")} · {stamp} ({TimeZoneLabel(tz, lang)})",
            $"📋 待完成 — 共 {total} 項 · {stamp}（{TimeZoneLabel(tz, lang)}）");
    }

    /// <summary>Friendly tz label for known zones, else the IANA id.</summary>
    private static string TimeZoneLabel(TimeZoneInfo tz, Language lang) => tz.Id switch
    {
        "Asia/Taipei" => lang.Pick("Taipei Time", "台北時間"),
        "America/New_York" => lang.Pick("Eastern Time", "美東時間"),
        _ => tz.Id,
    };

    private static IEnumerable<T> SortByDue<T>(IEnumerable<T> items, Func<T, DateTimeOffset?> due) =>
        items.OrderBy(i => due(i).HasValue ? 0 : 1)          // undated last
             .ThenBy(i => due(i) ?? DateTimeOffset.MaxValue); // then soonest first

    private static string AssignmentLine(
        OutstandingAssignment a, DateTimeOffset nowUtc, TimeZoneInfo tz, Language lang)
    {
        var link = a.HtmlUrl is null ? a.Name : $"[{a.Name}]({a.HtmlUrl})";
        var prefix = $"• [{a.CourseLabel}] {link} — ";

        if (a.DueAt is null)
            return prefix + lang.Pick("no due date", "無截止日");

        var hint = TimeFormatting.RelativeHint(a.DueAt.Value, nowUtc, tz, lang, out var overdue);
        if (overdue)
            return $"{prefix}{hint} ⚠️";

        var due = TimeFormatting.AbsoluteDue(a.DueAt.Value, tz, lang);
        return prefix + lang.Pick($"due {due} ({hint})", $"截止 {due}（{hint}）");
    }

    private static string PeerReviewLine(
        OutstandingPeerReview p, DateTimeOffset nowUtc, TimeZoneInfo tz, Language lang)
    {
        var head = lang.Pick(
            $"• [{p.CourseLabel}] Peer review: “{p.AssignmentName}” — {p.PendingCount} pending",
            $"• [{p.CourseLabel}] 互評：「{p.AssignmentName}」— 待完成 {p.PendingCount} 件");

        if (p.DueAt is null)
            return head + lang.Pick(" (no due date)", "（無截止日）");

        // DueAt is the peer-review deadline (§4.3); show it with a relative hint.
        var hint = TimeFormatting.RelativeHint(p.DueAt.Value, nowUtc, tz, lang, out var overdue);
        var date = TimeFormatting.ShortDate(p.DueAt.Value, tz);
        var line = head + lang.Pick($" (review due {date}, {hint})", $"（互評截止 {date}，{hint}）");
        return overdue ? $"{line} ⚠️" : line;
    }
}
