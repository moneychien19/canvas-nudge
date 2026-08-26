using CanvasNudge;
using Xunit;

namespace CanvasNudge.Tests;

public class ConfigTests
{
    private static Func<string, string?> Env(Dictionary<string, string?> map) => key =>
        map.TryGetValue(key, out var v) ? v : null;

    private static Dictionary<string, string?> WithRequired() => new()
    {
        ["CANVAS_API_TOKEN"] = "tok",
        ["DISCORD_WEBHOOK_URL"] = "https://discord/wh",
    };

    [Fact]
    public void Load_AppliesDocumentedDefaults_WhenOptionalsAbsent()
    {
        var cfg = Config.Load(Env(WithRequired()));

        Assert.Equal("tok", cfg.CanvasApiToken);
        Assert.Equal("https://discord/wh", cfg.DiscordWebhookUrl);
        Assert.Equal("https://canvas.cmu.edu", cfg.CanvasBaseUrl);
        Assert.Equal("America/New_York", cfg.Timezone);
        Assert.True(cfg.NotifyWhenEmpty);        // default true
        Assert.True(cfg.NotifyErrorsToDiscord);  // default true
        Assert.Null(cfg.CanvasTokenExpiresAt);
    }

    [Fact]
    public void Load_OverridesDefaults_WhenProvided()
    {
        var map = WithRequired();
        map["CANVAS_BASE_URL"] = "https://canvas.example.edu";
        map["TIMEZONE"] = "Asia/Taipei";
        map["NOTIFY_WHEN_EMPTY"] = "false";
        map["NOTIFY_ERRORS_TO_DISCORD"] = "false";
        map["CANVAS_TOKEN_EXPIRES_AT"] = "2026-10-01";

        var cfg = Config.Load(Env(map));

        Assert.Equal("https://canvas.example.edu", cfg.CanvasBaseUrl);
        Assert.Equal("Asia/Taipei", cfg.Timezone);
        Assert.False(cfg.NotifyWhenEmpty);
        Assert.False(cfg.NotifyErrorsToDiscord);
        Assert.Equal("2026-10-01", cfg.CanvasTokenExpiresAt);
    }

    [Theory]
    [InlineData("CANVAS_API_TOKEN")]
    [InlineData("DISCORD_WEBHOOK_URL")]
    public void Load_Throws_WhenRequiredMissing(string missingKey)
    {
        var map = WithRequired();
        map.Remove(missingKey);

        var ex = Assert.Throws<InvalidOperationException>(() => Config.Load(Env(map)));
        Assert.Contains(missingKey, ex.Message);
    }

    [Theory]
    [InlineData("CANVAS_API_TOKEN")]
    [InlineData("DISCORD_WEBHOOK_URL")]
    public void Load_TreatsEmptyStringAsMissing_ForRequired(string key)
    {
        var map = WithRequired();
        map[key] = "";

        Assert.Throws<InvalidOperationException>(() => Config.Load(Env(map)));
    }

    [Theory]
    [InlineData("TRUE", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData(" true ", true)]
    public void Load_ParsesBooleans_CaseAndSpaceInsensitive(string raw, bool expected)
    {
        var map = WithRequired();
        map["NOTIFY_WHEN_EMPTY"] = raw;

        Assert.Equal(expected, Config.Load(Env(map)).NotifyWhenEmpty);
    }

    [Fact]
    public void Load_Throws_OnInvalidBoolean()
    {
        var map = WithRequired();
        map["NOTIFY_WHEN_EMPTY"] = "yes";

        Assert.Throws<InvalidOperationException>(() => Config.Load(Env(map)));
    }

    [Fact]
    public void Load_TreatsEmptyExpiry_AsNull()
    {
        var map = WithRequired();
        map["CANVAS_TOKEN_EXPIRES_AT"] = "";

        Assert.Null(Config.Load(Env(map)).CanvasTokenExpiresAt);
    }
}
