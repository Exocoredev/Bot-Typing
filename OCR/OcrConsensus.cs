using System.Text;

namespace AutoInputTester.OCR;

/// <summary>
/// Soft consensus between two OCR passes.
/// Agree on matching glyphs; keep umlauts; on hard disagreement prefer the
/// shared prefix (accuracy) and only fall back to a full single pass.
/// </summary>
public static class OcrConsensus
{
    public readonly record struct Result(string Text, bool HadDisagreement, int DroppedCharacters);

    public static Result Agree(string first, string second)
    {
        first ??= string.Empty;
        second ??= string.Empty;

        if (first.Length == 0 && second.Length == 0)
            return new Result(string.Empty, false, 0);

        if (string.Equals(first, second, StringComparison.Ordinal))
            return new Result(first, false, 0);

        if (first.Length == 0) return new Result(second, true, 0);
        if (second.Length == 0) return new Result(first, true, 0);

        if (first.StartsWith(second, StringComparison.Ordinal))
            return new Result(second, true, first.Length - second.Length);
        if (second.StartsWith(first, StringComparison.Ordinal))
            return new Result(first, true, second.Length - first.Length);

        var sb = new StringBuilder(Math.Min(first.Length, second.Length));
        var ia = 0;
        var ib = 0;
        var skips = 0;
        var umlautFixed = false;

        while (ia < first.Length && ib < second.Length)
        {
            var a = first[ia];
            var b = second[ib];

            if (a == b)
            {
                sb.Append(a);
                ia++;
                ib++;
                continue;
            }

            if (OcrTextCleanup.SameLetterIgnoreUmlaut(a, b))
            {
                sb.Append(OcrTextCleanup.PreferUmlaut(a, b));
                ia++;
                ib++;
                umlautFixed = true;
                continue;
            }

            if (skips < 4 && ia + 1 < first.Length &&
                (first[ia + 1] == b || OcrTextCleanup.SameLetterIgnoreUmlaut(first[ia + 1], b)))
            {
                ia++;
                skips++;
                continue;
            }

            if (skips < 4 && ib + 1 < second.Length &&
                (a == second[ib + 1] || OcrTextCleanup.SameLetterIgnoreUmlaut(a, second[ib + 1])))
            {
                ib++;
                skips++;
                continue;
            }

            break;
        }

        var agreed = sb.ToString();
        var shorter = Math.Min(first.Length, second.Length);
        var dropped = Math.Max(0, Math.Max(first.Length, second.Length) - agreed.Length);

        // Strong agreement: only type what both passes support (cuts j/z flip errors).
        if (agreed.Length >= Math.Max(6, (int)(shorter * 0.8)))
            return new Result(agreed, umlautFixed || dropped > 0, dropped);

        // Weak agreement: take first pass (stable), prefer second only if clearly more umlauts.
        var preferred = PreferStable(first, second);
        return new Result(preferred, true, Math.Abs(first.Length - second.Length));
    }

    private static string PreferStable(string a, string b)
    {
        var scoreA = a.Count(OcrTextCleanup.HasUmlaut);
        var scoreB = b.Count(OcrTextCleanup.HasUmlaut);
        if (scoreB > scoreA && Math.Abs(a.Length - b.Length) <= Math.Max(4, a.Length / 10))
            return b;
        return a;
    }
}
