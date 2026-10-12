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

                // Letter-spaced typewriter fonts often create false word breaks.
                // Only keep a space when the gap looks like a real word separator.
                if (gap >= charWidth * 0.45)
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

        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var left = i > 0 ? chars[i - 1] : '\0';
            var right = i + 1 < chars.Length ? chars[i + 1] : '\0';

            var digitLeft = char.IsDigit(left);
            var digitRight = char.IsDigit(right);

            // "1"/"|" inside letter runs are almost always a misread "l".
            if (chars[i] is '1' or '|')
            {
                var letterLeft = char.IsLetter(left);
                var letterRight = char.IsLetter(right);
                if ((letterLeft || letterRight) && !digitLeft && !digitRight)
                    chars[i] = 'l';
            }

            // Capital "I" mid-word (both neighbours lowercase) is usually a misread "l".
            // Do NOT touch word-initial "Ich", "In", …
            if (chars[i] == 'I' && char.IsLower(left) && char.IsLower(right))
                chars[i] = 'l';

            // Isolated O/0 confusion next to digits.
            if (chars[i] == 'O' && (digitLeft || digitRight))
                chars[i] = '0';
            if (chars[i] == '0' && char.IsLetter(left) && char.IsLetter(right))
                chars[i] = 'o';
        }

        return new string(chars);
    }
}
