using CanvasNudge;
using Xunit;

namespace CanvasNudge.Tests;

public class DigestTests
{
    private static readonly TimeZoneInfo Taipei = TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    // "now" = 2026-06-15 14:17 Taipei (UTC+8) -> 06:17Z
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 6, 17, 0, TimeSpan.Zero);

    private static DateTimeOffset TaipeiUtc(int y, int mo, int d, int h, int mi) =>
        new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.FromHours(8)).ToUniversalTime();

    // --- relative-time hints (spec §5) ---

    [Fact]
    public void RelativeHint_FutureDays()
    {
        var hint = TimeFormatting.RelativeHint(TaipeiUtc(2026, 6, 17, 23, 59), Now, Taipei, Language.ZhHant, out var overdue);
        Assert.Equal("還有 2 天", hint);
        Assert.False(overdue);
    }

    [Fact]
    public void RelativeHint_FutureHours_SameDay()
    {
        var hint = TimeFormatting.RelativeHint(TaipeiUtc(2026, 6, 15, 18, 0), Now, Taipei, Language.ZhHant, out _);
        Assert.Equal("還有 3 小時", hint);
    }

    [Fact]
    public void RelativeHint_DueWithinTheHour_IsToday()
    {
        var hint = TimeFormatting.RelativeHint(TaipeiUtc(2026, 6, 15, 14, 30), Now, Taipei, Language.ZhHant, out _);
        Assert.Equal("今天截止", hint);
    }

    [Fact]
    public void RelativeHint_Overdue()
    {
        var hint = TimeFormatting.RelativeHint(TaipeiUtc(2026, 6, 14, 23, 59), Now, Taipei, Language.ZhHant, out var overdue);
        Assert.Equal("已逾期 1 天", hint);
        Assert.True(overdue);
    }

    // --- weekday mapping ---

    [Theory]
    [InlineData(DayOfWeek.Sunday, "週日")]
    [InlineData(DayOfWeek.Tuesday, "週二")]
    [InlineData(DayOfWeek.Saturday, "週六")]
    public void Weekday_MapsToZhHant(DayOfWeek day, string expected) =>
        Assert.Equal(expected, TimeFormatting.Weekday(day, Language.ZhHant));

    [Fact]
    public void AbsoluteDue_RendersDateWeekdayTime()
    {
        var text = TimeFormatting.AbsoluteDue(TaipeiUtc(2026, 6, 17, 23, 59), Taipei, Language.ZhHant);
        Assert.StartsWith("6/17（週", text);
        Assert.EndsWith("）23:59", text);
    }

    // --- sorting: due asc, undated last ---

    [Fact]
    public void Build_SortsByDueAscending_UndatedLast()
    {
        var assignments = new[]
        {
            new OutstandingAssignment("C1", "Undated", null, "u"),
            new OutstandingAssignment("C2", "Later", TaipeiUtc(2026, 6, 20, 9, 0), "l"),
            new OutstandingAssignment("C3", "Sooner", TaipeiUtc(2026, 6, 16, 9, 0), "s"),
        };

        var content = DigestBuilder.Build(assignments, Array.Empty<OutstandingPeerReview>(), Now, Taipei, Language.ZhHant);

        var iSooner = content.Description.IndexOf("Sooner", StringComparison.Ordinal);
        var iLater = content.Description.IndexOf("Later", StringComparison.Ordinal);
        var iUndated = content.Description.IndexOf("Undated", StringComparison.Ordinal);
        Assert.True(iSooner < iLater && iLater < iUndated);
    }

    // --- full shape (spec §5) ---

    [Fact]
    public void Build_ProducesHeaderCategoriesAndLines()
    {
        var assignments = new[]
        {
            new OutstandingAssignment("15-503", "Cache Lab", TaipeiUtc(2026, 6, 17, 23, 59),
                "https://canvas/a"),
            new OutstandingAssignment("15-619", "Project Phase 1", TaipeiUtc(2026, 6, 14, 9, 0),
                "https://canvas/b"),
            new OutstandingAssignment("17-214", "Undated HW", null, "https://canvas/c"),
        };
        var peer = new[]
        {
            new OutstandingPeerReview("15-513", "Design Doc", TaipeiUtc(2026, 6, 17, 23, 59), 3),
        };

        var content = DigestBuilder.Build(assignments, peer, Now, Taipei, Language.ZhHant);

        Assert.Equal(4, content.TotalCount);                       // 3 assignments + 1 peer group
        Assert.Contains("📋 待完成 — 共 4 項", content.Header);
        Assert.Contains("（台北時間）", content.Header);
        Assert.Contains("2026/06/15 14:17", content.Header);

        Assert.Contains("作業 (3)", content.Description);
        Assert.Contains("同儕互評 (1)", content.Description);

        Assert.Contains("[Cache Lab](https://canvas/a)", content.Description);  // markdown link
        Assert.Contains("[15-503]", content.Description);                       // course code literal
        Assert.Contains("還有 2 天", content.Description);
        Assert.Contains("已逾期", content.Description);
        Assert.Contains("⚠️", content.Description);
        Assert.Contains("無截止日", content.Description);

        Assert.Contains("互評：「Design Doc」", content.Description);
        Assert.Contains("待完成 3 件", content.Description);
        Assert.Contains("互評截止 6/17", content.Description);   // peer-review deadline + relative hint
        Assert.Contains("還有 2 天", content.Description);
    }

    [Fact]
    public void Build_LabelsHeader_美東時間_WhenTimezoneIsNewYork()
    {
        var content = DigestBuilder.Build(
            Array.Empty<OutstandingAssignment>(), Array.Empty<OutstandingPeerReview>(), Now, NewYork, Language.ZhHant);

        Assert.Contains("（美東時間）", content.Header);
    }

    [Fact]
    public void PeerReviewLine_MarksOverdue()
    {
        var peer = new[]
        {
            new OutstandingPeerReview("C", "Late Review", TaipeiUtc(2026, 6, 12, 12, 10), 2),
        };

        var content = DigestBuilder.Build(Array.Empty<OutstandingAssignment>(), peer, Now, Taipei, Language.ZhHant);

        Assert.Contains("已逾期 3 天", content.Description);
        Assert.Contains("⚠️", content.Description);
    }

    // --- English (default language) ---

    [Theory]
    [InlineData(2026, 6, 17, 23, 59, "in 2 days")]
    [InlineData(2026, 6, 16, 9, 0, "in 1 day")]
    [InlineData(2026, 6, 15, 18, 0, "in 3 hours")]
    [InlineData(2026, 6, 15, 15, 30, "in 1 hour")]
    [InlineData(2026, 6, 15, 14, 30, "due today")]
    [InlineData(2026, 6, 14, 23, 59, "overdue by 1 day")]
    [InlineData(2026, 6, 12, 12, 0, "overdue by 3 days")]
    public void RelativeHint_English(int y, int mo, int d, int h, int mi, string expected) =>
        Assert.Equal(expected,
            TimeFormatting.RelativeHint(TaipeiUtc(y, mo, d, h, mi), Now, Taipei, Language.En, out _));

    [Fact]
    public void AbsoluteDue_English_RendersWeekdayDateTime()
    {
        var text = TimeFormatting.AbsoluteDue(TaipeiUtc(2026, 6, 17, 23, 59), Taipei, Language.En);
        Assert.Equal("Wed 6/17 23:59", text);
    }

    [Fact]
    public void Build_English_ProducesHeaderCategoriesAndLines()
    {
        var assignments = new[]
        {
            new OutstandingAssignment("15-503", "Cache Lab", TaipeiUtc(2026, 6, 17, 23, 59),
                "https://canvas/a"),
            new OutstandingAssignment("15-619", "Project Phase 1", TaipeiUtc(2026, 6, 14, 9, 0),
                "https://canvas/b"),
            new OutstandingAssignment("17-214", "Undated HW", null, "https://canvas/c"),
        };
        var peer = new[]
        {
            new OutstandingPeerReview("15-513", "Design Doc", TaipeiUtc(2026, 6, 17, 23, 59), 3),
        };

        var content = DigestBuilder.Build(assignments, peer, Now, NewYork, Language.En);

        Assert.StartsWith("📋 Outstanding — 4 items · ", content.Header);
        Assert.EndsWith("(Eastern Time)", content.Header);

        Assert.Contains("Assignments (3)", content.Description);
        Assert.Contains("Peer Reviews (1)", content.Description);
        Assert.Contains("• [15-503] [Cache Lab](https://canvas/a) — due ", content.Description);
        Assert.Contains("overdue by", content.Description);
        Assert.Contains("⚠️", content.Description);
        Assert.Contains("[Undated HW](https://canvas/c) — no due date", content.Description);
        Assert.Contains("• [15-513] Peer review: “Design Doc” — 3 pending (review due 6/17, in 2 days)",
            content.Description);
        Assert.DoesNotContain("作業", content.Description);
    }

    [Fact]
    public void Build_English_SingularItem()
    {
        var content = DigestBuilder.Build(
            new[] { new OutstandingAssignment("C", "HW", null, "u") },
            Array.Empty<OutstandingPeerReview>(), Now, Taipei, Language.En);

        Assert.Contains("1 item ·", content.Header);
        Assert.Contains("(Taipei Time)", content.Header);
    }

    [Fact]
    public void ToPayload_WrapsAsSingleEmbed()
    {
        var content = new DigestContent("HEADER", "BODY", 1);

        var payload = DigestBuilder.ToPayload(content);

        var embed = Assert.Single(payload.Embeds!);
        Assert.Equal("HEADER", embed.Title);
        Assert.Equal("BODY", embed.Description);
        Assert.Null(payload.Content);
    }
}
