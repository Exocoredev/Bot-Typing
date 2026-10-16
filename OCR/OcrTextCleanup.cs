using System.Globalization;
using System.Text;
using Windows.Media.Ocr;

namespace AutoInputTester.OCR;

public static class OcrTextCleanup
{
    public static string FromResult(OcrResult result)
    {
        if (result.Lines.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        var firstLine = true;
        foreach (var line in result.Lines)
        {
            if (!firstLine)
                sb.Append('\n');
            firstLine = false;
            AppendLine(sb, line);
        }

        return FixConfusables(sb.ToString());
    }

    private static void AppendLine(StringBuilder sb, OcrLine line)
    {
        OcrWord? previous = null;
        foreach (var word in line.Words)
        {
            if (previous is not null)
            {
                var gap = word.BoundingRect.X - (previous.BoundingRect.X + previous.BoundingRect.Width);
                var prevCharWidth = previous.BoundingRect.Width / Math.Max(1, previous.Text.Length);
                var wordCharWidth = word.BoundingRect.Width / Math.Max(1, word.Text.Length);
                var charWidth = Math.Max(1.0, (prevCharWidth + wordCharWidth) / 2.0);

                if (gap >= charWidth * 0.55)
                    sb.Append(' ');
            }

            sb.Append(word.Text);
            previous = word;
        }
    }

    public static string FixConfusables(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // o + combining diaeresis → ö, etc.
        text = text.Normalize(NormalizationForm.FormC);

        var chars = text.ToCharArray();
        var list = new List<char>(chars.Length);

        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            var next = i + 1 < chars.Length ? chars[i + 1] : '\0';

            // OCR-A/B often emits vowel + diaeresis/quotes instead of äöü.
            if (IsAsciiVowel(c) && IsDiaeresisMark(next))
            {
                list.Add(WithUmlaut(c));
                i++; // skip mark
                continue;
            }

            // Stray combining mark without vowel — drop.
            if (IsDiaeresisMark(c))
                continue;

            list.Add(MapUmlautLookalike(c));
        }

        chars = list.ToArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var left = i > 0 ? chars[i - 1] : '\0';
            var right = i + 1 < chars.Length ? chars[i + 1] : '\0';

            var digitLeft = char.IsDigit(left);
            var digitRight = char.IsDigit(right);
            var letterLeft = char.IsLetter(left);
            var letterRight = char.IsLetter(right);

            if (chars[i] is '1' or '|' or '!')
            {
                if ((letterLeft || letterRight) && !digitLeft && !digitRight)
                    chars[i] = 'l';
            }

            if (chars[i] == 'I' && char.IsLower(left) && char.IsLower(right))
                chars[i] = 'l';

            if (chars[i] == 'l' && digitLeft && digitRight)
                chars[i] = '1';

            // Don't turn Ö into 0 — only plain ASCII O next to digits.
            if (chars[i] == 'O' && (digitLeft || digitRight))
                chars[i] = '0';
            if (chars[i] == '0' && letterLeft && letterRight)
                chars[i] = 'o';

            if (chars[i] is '\u00AD' or '\u2013' or '\u2014')
                chars[i] = '-';

            // OCR-A: j often becomes ] or ); z often becomes 2 amid letters.
            if ((chars[i] is ']' or ')') && letterLeft && letterRight)
                chars[i] = 'j';
            if (chars[i] == '2' && letterLeft && letterRight)
                chars[i] = 'z';
        }

        return new string(chars);
    }

    /// <summary>
    /// OCR-A/B often returns lookalikes instead of German äöü.
    /// </summary>
    private static char MapUmlautLookalike(char c) => c switch
    {
        'ø' or 'ő' or 'ǒ' or 'õ' or 'ô' or 'ӧ' or 'ȯ' => 'ö',
        'Ø' or 'Ő' or 'Ǒ' or 'Õ' or 'Ô' or 'Ӧ' or 'Ȯ' => 'Ö',
        'ǎ' or 'ã' or 'â' or 'ȧ' or 'ӓ' => 'ä',
        'Ǎ' or 'Ã' or 'Â' or 'Ȧ' or 'Ӓ' => 'Ä',
        'ǔ' or 'ũ' or 'û' or 'ů' or 'ӱ' => 'ü',
        'Ǔ' or 'Ũ' or 'Û' or 'Ů' or 'Ӱ' => 'Ü',
        _ => c
    };

    private static bool IsAsciiVowel(char c) =>
        c is 'a' or 'A' or 'o' or 'O' or 'u' or 'U';

    private static bool IsDiaeresisMark(char c) =>
        c is '\u0308' // combining diaeresis
            or '\u00A8' // ¨
            or '"'
            or '¨'
            or '\u02D9'; // dot above sometimes misread

    private static char WithUmlaut(char vowel) => vowel switch
    {
        'a' => 'ä',
        'A' => 'Ä',
        'o' => 'ö',
        'O' => 'Ö',
        'u' => 'ü',
        'U' => 'Ü',
        _ => vowel
    };

    /// <summary>True if two glyphs are the same letter ignoring umlaut (o≈ö).</summary>
    public static bool SameLetterIgnoreUmlaut(char a, char b)
    {
        if (a == b) return true;
        return StripUmlaut(a) == StripUmlaut(b)
               && char.GetUnicodeCategory(a) != UnicodeCategory.OtherPunctuation;
    }

    public static char PreferUmlaut(char a, char b)
    {
        if (HasUmlaut(a)) return a;
        if (HasUmlaut(b)) return b;
        return a;
    }

    public static bool HasUmlaut(char c) =>
        c is 'ä' or 'ö' or 'ü' or 'Ä' or 'Ö' or 'Ü'
            or 'ø' or 'ő' or 'Ø' or 'Ő';

    public static char StripUmlaut(char c) => c switch
    {
        'ä' or 'Ä' or 'ǎ' or 'ã' or 'â' or 'Ǎ' or 'Ã' or 'Â' => char.IsUpper(c) ? 'A' : 'a',
        'ö' or 'Ö' or 'ø' or 'ő' or 'ǒ' or 'õ' or 'ô' or 'Ø' or 'Ő' or 'Ǒ' or 'Õ' or 'Ô'
            => char.IsUpper(c) ? 'O' : 'o',
        'ü' or 'Ü' or 'ǔ' or 'ũ' or 'û' or 'ů' or 'Ǔ' or 'Ũ' or 'Û' or 'Ů'
            => char.IsUpper(c) ? 'U' : 'u',
        _ => c
    };
}
