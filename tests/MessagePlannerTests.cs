using CanvasNudge;
using Xunit;

namespace CanvasNudge.Tests;

public class MessagePlannerTests
{
    private static readonly TimeZoneInfo Taipei = TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 6, 17, 0, TimeSpan.Zero);

    private static DigestContent WithItems() => DigestBuilder.Build(
        new[] { new OutstandingAssignment("C", "HW", null, "u") },
        Array.Empty<OutstandingPeerReview>(), Now, Taipei, Language.ZhHant);

    private static DigestContent Empty() => DigestBuilder.Build(
        Array.Empty<OutstandingAssignment>(), Array.Empty<OutstandingPeerReview>(), Now, Taipei, Language.ZhHant);

    [Fact]
    public void Plan_SendsDigestEmbed_WhenItemsExist()
    {
        var payload = MessagePlanner.Plan(WithItems(), notifyWhenEmpty: false, expiryWarning: null, Language.ZhHant);

        Assert.NotNull(payload);
        var embed = Assert.Single(payload!.Embeds!);
        Assert.Contains("共 1 項", embed.Title);
        Assert.Contains("HW", embed.Description);
    }

    [Fact]
    public void Plan_SendsCelebratory_WhenEmptyAndNotifyWhenEmptyTrue()
    {
        var payload = MessagePlanner.Plan(Empty(), notifyWhenEmpty: true, expiryWarning: null, Language.ZhHant);

        Assert.NotNull(payload);
        Assert.Equal(MessagePlanner.EmptyMessage(Language.ZhHant), payload!.Content);
        Assert.Contains("恭喜", payload.Content);
    }

    [Fact]
    public void Plan_ReturnsNull_WhenEmptyAndSilent()
    {
        var payload = MessagePlanner.Plan(Empty(), notifyWhenEmpty: false, expiryWarning: null, Language.ZhHant);

        Assert.Null(payload);  // silent on empty
    }

    [Fact]
    public void Plan_SendsEnglishCelebratory_WithWarning()
    {
        var empty = DigestBuilder.Build(
            Array.Empty<OutstandingAssignment>(), Array.Empty<OutstandingPeerReview>(), Now, Taipei, Language.En);

        var payload = MessagePlanner.Plan(empty, notifyWhenEmpty: false, expiryWarning: "⚠️ W", Language.En);

        Assert.NotNull(payload);
        Assert.Equal("⚠️ W\nAll caught up — nothing outstanding 🎉", payload!.Content);
    }
}
