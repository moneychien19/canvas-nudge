using CanvasNudge;
using Xunit;

namespace CanvasNudge.Tests;

public class ErrorReporterTests
{
    [Fact]
    public void ExpiredPatMessage_MatchesSpec()
    {
        var msg = ErrorReporter.ExpiredPatMessage;

        Assert.Contains("❌ Canvas PAT 失效（HTTP 401）", msg);
        Assert.Contains("本 token 設定 120 天到期", msg);
        Assert.Contains("Account → Settings → Approved Integrations → New Access Token", msg);
        Assert.Contains("CANVAS_API_TOKEN", msg);
        Assert.Contains("CANVAS_TOKEN_EXPIRES_AT", msg);
    }

    [Fact]
    public void LogText_ForAuthException_IsTheFixItBlock()
    {
        var text = ErrorReporter.LogText(new CanvasAuthException("401"));
        Assert.Equal(ErrorReporter.ExpiredPatMessage, text);
    }

    [Fact]
    public void LogText_ForOtherException_IsShortChineseLine()
    {
        var text = ErrorReporter.LogText(new InvalidOperationException("boom"));
        Assert.Contains("執行失敗", text);
        Assert.Contains("boom", text);
    }

    [Fact]
    public void DiscordPayload_ForAuthException_CarriesFixItBlock()
    {
        var payload = ErrorReporter.DiscordPayload(new CanvasAuthException("401"));
        Assert.Contains("HTTP 401", payload.Content);
    }

    [Fact]
    public void DiscordPayload_ForOtherException_IsShortLine()
    {
        var payload = ErrorReporter.DiscordPayload(new Exception("network down"));
        Assert.Contains("執行失敗", payload.Content);
        Assert.Contains("network down", payload.Content);
    }
}
