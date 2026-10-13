using CanvasNudge;
using Xunit;

namespace CanvasNudge.Tests;

public class ExpiryWarningTests
{
    private static readonly TimeZoneInfo Taipei = TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");
    // now = 2026-06-15 14:17 Taipei
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 6, 17, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-date")]
    public void Compute_ReturnsNull_WhenUnsetOrInvalid(string? raw) =>
        Assert.Null(ExpiryWarning.Compute(raw, Now, Taipei, Language.ZhHant));

    [Fact]
    public void Compute_Silent_WhenMoreThan14DaysOut()
    {
        // 2026-07-15 is 30 days away
        Assert.Null(ExpiryWarning.Compute("2026-07-15", Now, Taipei, Language.ZhHant));
    }

    [Fact]
    public void Compute_Warns_WhenWithin14Days()
    {
        // 2026-06-25 is 10 days away
        var warning = ExpiryWarning.Compute("2026-06-25", Now, Taipei, Language.ZhHant);

        Assert.NotNull(warning);
        Assert.Contains("⚠️", warning);
        Assert.Contains("將於 10 天後", warning);
        Assert.Contains("2026-06-25", warning);
        Assert.Contains("過期", warning);
    }

    [Fact]
    public void Compute_Warns_OnExpiryDay()
    {
        var warning = ExpiryWarning.Compute("2026-06-15", Now, Taipei, Language.ZhHant);

        Assert.NotNull(warning);
        Assert.Contains("今天", warning!);
        Assert.Contains("2026-06-15", warning);
    }

    [Fact]
    public void Compute_Warns_WhenAlreadyPast()
    {
        var warning = ExpiryWarning.Compute("2026-06-10", Now, Taipei, Language.ZhHant);

        Assert.NotNull(warning);
        Assert.Contains("已過期", warning!);
        Assert.Contains("2026-06-10", warning);
    }

    [Theory]
    [InlineData("2026-06-25", "⚠️ Canvas PAT expires in 10 days (2026-06-25)")]
    [InlineData("2026-06-16", "⚠️ Canvas PAT expires in 1 day (2026-06-16)")]
    [InlineData("2026-06-15", "⚠️ Canvas PAT expires today (2026-06-15)")]
    [InlineData("2026-06-10", "⚠️ Canvas PAT may have expired (2026-06-10)")]
    public void Compute_English(string raw, string expectedPrefix)
    {
        var warning = ExpiryWarning.Compute(raw, Now, Taipei, Language.En);

        Assert.NotNull(warning);
        Assert.StartsWith(expectedPrefix, warning);
    }
}
