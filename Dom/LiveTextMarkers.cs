namespace AutoInputTester.Dom;

/// <summary>
/// Live-reader payload markers. TypingEngine treats DOM-remaining as an exact
/// replacement of the untyped buffer (no fuzzy OCR merge).
/// </summary>
public static class LiveTextMarkers
{
    public const char DomRemainingPrefix = '\uE000';

    public static string MarkDomRemaining(string text) =>
        DomRemainingPrefix + (text ?? string.Empty);

    public static bool TryUnmarkDomRemaining(ref string text)
    {
        if (string.IsNullOrEmpty(text) || text[0] != DomRemainingPrefix)
            return false;
        text = text.Length == 1 ? string.Empty : text[1..];
        return true;
    }
}
