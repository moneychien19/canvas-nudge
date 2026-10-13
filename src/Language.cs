namespace CanvasNudge;

/// <summary>Language for all human-facing text (Discord messages + run log).</summary>
internal enum Language
{
    En,
    ZhHant,
}

internal static class LanguageExtensions
{
    /// <summary>Pick the string for this language. Canvas-supplied names are never translated.</summary>
    public static string Pick(this Language lang, string en, string zhHant) =>
        lang == Language.En ? en : zhHant;

    /// <summary>English singular/plural, e.g. "1 day" / "3 days".</summary>
    public static string Plural(int n, string singular) => n == 1 ? $"{n} {singular}" : $"{n} {singular}s";
}
