namespace CanvasNudge;

/// <summary>
/// Builds human-facing failure messages (spec §8). A Canvas 401 yields the exact
/// self-explanatory expired-PAT fix-it block (§8.1); anything else yields a short
/// localized line. Used for both the run log and the optional Discord error post.
/// </summary>
internal static class ErrorReporter
{
    private const string ExpiredPatMessageEn =
        "❌ Canvas PAT rejected (HTTP 401)\n" +
        "Cause: the Personal Access Token has likely expired (this token was set to expire after 120 days) or been revoked.\n" +
        "Fix:\n" +
        "  1. Log in to Canvas → Account → Settings → Approved Integrations → New Access Token\n" +
        "  2. Generate a new token (set an expiry date again)\n" +
        "  3. Update GitHub repo → Settings → Secrets → CANVAS_API_TOKEN\n" +
        "  4. If you set CANVAS_TOKEN_EXPIRES_AT, change it to the new expiry date";

    private const string ExpiredPatMessageZhHant =
        "❌ Canvas PAT 失效（HTTP 401）\n" +
        "原因：Personal Access Token 可能已過期（本 token 設定 120 天到期）或被撤銷。\n" +
        "修復：\n" +
        "  1. 登入 Canvas → Account → Settings → Approved Integrations → New Access Token\n" +
        "  2. 產生新 token（記得再設一次到期日）\n" +
        "  3. 更新 GitHub repo → Settings → Secrets → CANVAS_API_TOKEN\n" +
        "  4. 若有設 CANVAS_TOKEN_EXPIRES_AT，把它改成新的到期日";

    /// <summary>The exact expired-PAT fix-it message (spec §8.1).</summary>
    public static string ExpiredPatMessage(Language lang) =>
        lang.Pick(ExpiredPatMessageEn, ExpiredPatMessageZhHant);

    /// <summary>Text for the run log.</summary>
    public static string LogText(Exception ex, Language lang) => ex is CanvasAuthException
        ? ExpiredPatMessage(lang)
        : lang.Pick($"❌ Canvas Nudge run failed: {ex.Message}", $"❌ Canvas Nudge 執行失敗：{ex.Message}");

    /// <summary>Payload for the optional Discord error post (spec §8 / §8.1).</summary>
    public static DiscordWebhookPayload DiscordPayload(Exception ex, Language lang) =>
        new(Content: LogText(ex, lang));
}
