using System.Globalization;
using System.Text;

namespace CanvasNudge;

/// <summary>
/// Traditional-Chinese date/time formatting for the digest (spec §5). All
/// computation is done in the target timezone; weekdays use a fixed zh-Hant
/// table so output doesn't depend on ICU culture data.
/// </summary>
internal static class TimeFormatting
{
    private static readonly string[] Weekdays = { "週日", "週一", "週二", "週三", "週四", "週五", "週六" };

    public static string Weekday(DayOfWeek day) => Weekdays[(int)day];

    /// <summary>Absolute due like "6/17（週二）23:59" in the target timezone.</summary>
    public static string AbsoluteDue(DateTimeOffset dueUtc, TimeZoneInfo tz)
    {
        var local = TimeZoneInfo.ConvertTime(dueUtc, tz);
        return $"{local.Month}/{local.Day}（{Weekday(local.DayOfWeek)}）{local:HH:mm}";
    }

    /// <summary>Short due like "6/14" (used for peer-review context).</summary>
    public static string ShortDate(DateTimeOffset dueUtc, TimeZoneInfo tz)
    {
        var local = TimeZoneInfo.ConvertTime(dueUtc, tz);
        return $"{local.Month}/{local.Day}";
    }

    /// <summary>
    /// Relative hint in zh-Hant (spec §5): 還有 N 天 / 還有 N 小時 / 今天截止 /
    /// 已逾期 N 天. Day counts use calendar-day differences in the target tz, so
    /// "還有 2 天" matches the spec example. Same-day overdue (passed earlier
    /// today) renders as a bare 已逾期.
    /// </summary>
    public static string RelativeHint(DateTimeOffset dueUtc, DateTimeOffset nowUtc, TimeZoneInfo tz, out bool overdue)
    {
        var dueLocal = TimeZoneInfo.ConvertTime(dueUtc, tz);
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, tz);
        var delta = dueUtc - nowUtc;

        if (delta.Ticks < 0)
        {
            overdue = true;
            var overdueDays = (nowLocal.Date - dueLocal.Date).Days;
            return overdueDays >= 1 ? $"已逾期 {overdueDays} 天" : "已逾期";
        }

        overdue = false;
        var dayDiff = (dueLocal.Date - nowLocal.Date).Days;
        if (dayDiff >= 1) return $"還有 {dayDiff} 天";

        var hours = (int)Math.Floor(delta.TotalHours);
        return hours >= 1 ? $"還有 {hours} 小時" : "今天截止";
    }
}

/// <summary>
/// Builds the digest message body (spec §5): header + category groups, each
/// sorted by due date ascending with undated items last. Template text is
/// zh-Hant; Canvas-supplied names are emitted verbatim.
/// </summary>
internal static class DigestBuilder
{
    public static DigestContent Build(
        IReadOnlyCollection<OutstandingAssignment> assignments,
        IReadOnlyCollection<OutstandingPeerReview> peerReviews,
        DateTimeOffset nowUtc,
        TimeZoneInfo tz)
    {
        var total = assignments.Count + peerReviews.Count;
        var header = BuildHeader(total, nowUtc, tz);

        var body = new StringBuilder();

        if (assignments.Count > 0)
        {
            body.Append("作業 (").Append(assignments.Count).Append(")\n");
            foreach (var a in SortByDue(assignments, x => x.DueAt))
                body.Append(AssignmentLine(a, nowUtc, tz)).Append('\n');
        }

        if (peerReviews.Count > 0)
        {
            if (body.Length > 0) body.Append('\n');
            body.Append("同儕互評 (").Append(peerReviews.Count).Append(")\n");
            foreach (var p in SortByDue(peerReviews, x => x.DueAt))
                body.Append(PeerReviewLine(p, nowUtc, tz)).Append('\n');
        }

        return new DigestContent(header, body.ToString().TrimEnd('\n'), total);
    }

    /// <summary>Wrap the digest as a single Discord embed (header as title, body as description).</summary>
    public static DiscordWebhookPayload ToPayload(DigestContent content) =>
        new(Embeds: new[] { new DiscordEmbed(Title: content.Header, Description: content.Description) });

    private static string BuildHeader(int total, DateTimeOffset nowUtc, TimeZoneInfo tz)
    {
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, tz);
        var stamp = nowLocal.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
        return $"📋 待完成 — 共 {total} 項 · {stamp}（{TimeZoneLabel(tz)}）";
    }

    /// <summary>Friendly tz label; Taipei gets the spec's 台北時間, else the IANA id.</summary>
    private static string TimeZoneLabel(TimeZoneInfo tz) =>
        tz.Id == "Asia/Taipei" ? "台北時間" : tz.Id;

    private static IEnumerable<T> SortByDue<T>(IEnumerable<T> items, Func<T, DateTimeOffset?> due) =>
        items.OrderBy(i => due(i).HasValue ? 0 : 1)          // undated last
             .ThenBy(i => due(i) ?? DateTimeOffset.MaxValue); // then soonest first

    private static string AssignmentLine(OutstandingAssignment a, DateTimeOffset nowUtc, TimeZoneInfo tz)
    {
        var link = a.HtmlUrl is null ? a.Name : $"[{a.Name}]({a.HtmlUrl})";
        var prefix = $"• [{a.CourseLabel}] {link} — ";

        if (a.DueAt is null)
            return prefix + "無截止日";

        var hint = TimeFormatting.RelativeHint(a.DueAt.Value, nowUtc, tz, out var overdue);
        return overdue
            ? $"{prefix}{hint} ⚠️"
            : $"{prefix}截止 {TimeFormatting.AbsoluteDue(a.DueAt.Value, tz)}（{hint}）";
    }

    private static string PeerReviewLine(OutstandingPeerReview p, DateTimeOffset nowUtc, TimeZoneInfo tz)
    {
        var head = $"• [{p.CourseLabel}] 互評：「{p.AssignmentName}」— 待完成 {p.PendingCount} 件";

        if (p.DueAt is null)
            return $"{head}（無截止日）";

        // DueAt is the peer-review deadline (§4.3); show it with a relative hint.
        var hint = TimeFormatting.RelativeHint(p.DueAt.Value, nowUtc, tz, out var overdue);
        var line = $"{head}（互評截止 {TimeFormatting.ShortDate(p.DueAt.Value, tz)}，{hint}）";
        return overdue ? $"{line} ⚠️" : line;
    }
}
