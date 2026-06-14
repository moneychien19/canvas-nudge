using System.Globalization;

namespace CanvasNudge;

/// <summary>
/// Proactive PAT-expiry warning (spec §8.2). Given the stored expiry date, emits
/// a one-line zh-Hant warning when the token is ≤14 days from expiry (or past),
/// otherwise stays silent. Computed in the target timezone.
/// </summary>
internal static class ExpiryWarning
{
    private const int WarnWithinDays = 14;

    /// <summary>Returns the warning line, or null to stay silent / when unset or invalid.</summary>
    public static string? Compute(string? expiresAtRaw, DateTimeOffset nowUtc, TimeZoneInfo tz)
    {
        if (string.IsNullOrWhiteSpace(expiresAtRaw)) return null;
        if (!DateOnly.TryParseExact(expiresAtRaw.Trim(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry))
            return null;

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, tz).Date);
        var days = expiry.DayNumber - today.DayNumber;
        var date = expiry.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (days > WarnWithinDays) return null;

        return days switch
        {
            > 0 => $"⚠️ Canvas PAT 將於 {days} 天後（{date}）過期，請及早更新，否則通知會停止。",
            0 => $"⚠️ Canvas PAT 今天（{date}）就會過期，請盡快更新，否則通知會停止。",
            _ => $"⚠️ Canvas PAT 可能已過期（{date}），請盡快更新，否則通知會停止。",
        };
    }
}
