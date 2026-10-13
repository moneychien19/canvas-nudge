using CanvasNudge;
using Xunit;

namespace CanvasNudge.Tests;

public class ErrorReporterTests
{
    [Fact]
    public void ExpiredPatMessage_MatchesSpec()
    {
        var msg = ErrorReporter.ExpiredPatMessage(Language.ZhHant);

        Assert.Contains("❌ Canvas PAT 失效（HTTP 401）", msg);
        Assert.Contains("本 token 設定 120 天到期", msg);
        Assert.Contains("Account → Settings → Approved Integrations → New Access Token", msg);
        Assert.Contains("CANVAS_API_TOKEN", msg);
        Assert.Contains("CANVAS_TOKEN_EXPIRES_AT", msg);
    }

    [Fact]
    public void LogText_ForAuthException_IsTheFixItBlock()
    {
        var text = ErrorReporter.LogText(new CanvasAuthException("401"), Language.ZhHant);
        Assert.Equal(ErrorReporter.ExpiredPatMessage(Language.ZhHant), text);
    }

    [Fact]
    public void LogText_ForOtherException_IsShortChineseLine()
    {
        var text = ErrorReporter.LogText(new InvalidOperationException("boom"), Language.ZhHant);
        Assert.Contains("執行失敗", text);
        Assert.Contains("boom", text);
    }

    [Fact]
    public void DiscordPayload_ForAuthException_CarriesFixItBlock()
    {
        var payload = ErrorReporter.DiscordPayload(new CanvasAuthException("401"), Language.ZhHant);
        Assert.Contains("HTTP 401", payload.Content);
    }

    [Fact]
    public void DiscordPayload_ForOtherException_IsShortLine()
    {
        var payload = ErrorReporter.DiscordPayload(new Exception("network down"), Language.ZhHant);
        Assert.Contains("執行失敗", payload.Content);
        Assert.Contains("network down", payload.Content);
    }

    [Fact]
    public void ExpiredPatMessage_English_HasFixItSteps()
    {
        var msg = ErrorReporter.ExpiredPatMessage(Language.En);

        Assert.StartsWith("❌ Canvas PAT rejected (HTTP 401)", msg);
        Assert.Contains("Account → Settings → Approved Integrations → New Access Token", msg);
        Assert.Contains("CANVAS_API_TOKEN", msg);
        Assert.Contains("CANVAS_TOKEN_EXPIRES_AT", msg);
    }

    [Fact]
    public void LogText_English_ForOtherException()
    {
        var text = ErrorReporter.LogText(new InvalidOperationException("boom"), Language.En);
        Assert.Equal("❌ Canvas Nudge run failed: boom", text);
    }
}
