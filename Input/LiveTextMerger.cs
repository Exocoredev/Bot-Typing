namespace AutoInputTester.Input;

public readonly record struct LiveMergeResult(string Addition, bool Aligned, bool HadText);

public static class LiveTextMerger
{
    private const int MinOverlap = 12;

    // 'anchor' is the exact still-untyped text that was known when the screenshot
    // for 'fresh' was started. Therefore the OCR result should either contain the
    // anchor, be contained by it, or start inside the anchor if the UI scrolled.
    public static LiveMergeResult Merge(string anchor, string fresh)
    {
        anchor ??= string.Empty;
        fresh ??= string.Empty;

        if (fresh.Length == 0)
            return new LiveMergeResult(string.Empty, true, false);

        if (anchor.Length == 0)
            return new LiveMergeResult(string.Empty, false, true);

        // Prefer the LAST occurrence so short anchors (e.g. "en") do not match
        // earlier in the visible line and re-append already typed text.
        var anchorInsideFresh = fresh.LastIndexOf(anchor, StringComparison.Ordinal);
        if (anchorInsideFresh < 0)
            anchorInsideFresh = FindFuzzyLast(fresh, anchor, MaxErrorsFor(anchor.Length));

        if (anchorInsideFresh >= 0)
        {
            var end = anchorInsideFresh + anchor.Length;
            return new LiveMergeResult(end < fresh.Length ? fresh[end..] : string.Empty, true, true);
        }

        // OCR may currently see less than we already know. That is not an error
        // and must never cause duplicate text to be appended.
        if (anchor.Contains(fresh, StringComparison.Ordinal))
            return new LiveMergeResult(string.Empty, true, true);

        // The screen may have scrolled between anchor creation and screenshot.
        // In that case the OCR can begin a few characters later inside anchor.
        var overlap = LongestSuffixPrefix(anchor, fresh);
        if (overlap < MinOverlap)
            overlap = LongestFuzzySuffixPrefix(anchor, fresh, MaxErrorsFor(Math.Min(anchor.Length, fresh.Length)));

        var maxPossible = Math.Min(anchor.Length, fresh.Length);
        var required = maxPossible < MinOverlap ? maxPossible : MinOverlap;
        if (required > 0 && overlap >= required)
            return new LiveMergeResult(fresh[overlap..], true, true);

        return new LiveMergeResult(string.Empty, false, true);
    }

    /// <summary>
    /// Returns only text that is genuinely new relative to already known/typed text.
    /// Never returns a full OCR string that would retype known content.
    /// </summary>
    public static string NewTextAfterKnown(string known, string fresh)
    {
        known ??= string.Empty;
        fresh ??= string.Empty;

        if (fresh.Length == 0)
            return string.Empty;

        if (known.Length == 0)
            return fresh;

        if (known.Contains(fresh, StringComparison.Ordinal))
            return string.Empty;

        var knownInFresh = fresh.LastIndexOf(known, StringComparison.Ordinal);
        if (knownInFresh < 0)
            knownInFresh = FindFuzzyLast(fresh, known, MaxErrorsFor(known.Length));
        if (knownInFresh >= 0)
        {
            var end = knownInFresh + known.Length;
            return end < fresh.Length ? fresh[end..] : string.Empty;
        }

        var minAnchor = Math.Min(MinOverlap, known.Length);
        var maxAnchor = Math.Min(known.Length, 120);
        for (var len = maxAnchor; len >= minAnchor; len--)
        {
            var suffix = known[^len..];
            var pos = fresh.LastIndexOf(suffix, StringComparison.Ordinal);
            if (pos < 0)
                pos = FindFuzzyLast(fresh, suffix, MaxErrorsFor(len));
            if (pos < 0)
                continue;

            var end = pos + len;
            return end < fresh.Length ? fresh[end..] : string.Empty;
        }

        var overlap = LongestSuffixPrefix(known, fresh);
        if (overlap < MinOverlap)
            overlap = LongestFuzzySuffixPrefix(known, fresh, MaxErrorsFor(Math.Min(known.Length, fresh.Length)));

        var maxPossible = Math.Min(known.Length, fresh.Length);
        var required = maxPossible < MinOverlap ? maxPossible : MinOverlap;
        if (required > 0 && overlap >= required)
            return fresh[overlap..];

        return string.Empty;
    }

    public static bool SharesNoOverlap(string known, string fresh)
    {
        known ??= string.Empty;
        fresh ??= string.Empty;
        if (known.Length == 0 || fresh.Length == 0)
            return true;

        // Only treat as "no overlap" when a LONG typed suffix is missing from OCR.
        // Short probes (4–8 chars) false-positive on common German endings and
        // then blocked appending of the next BlackTextOnly chunk.
        var probe = Math.Min(24, known.Length);
        if (probe < 10)
            probe = known.Length;
        if (probe <= 0)
            return true;

        var suffix = known[^probe..];
        if (fresh.Contains(suffix, StringComparison.Ordinal))
            return false;
        if (FindFuzzyLast(fresh, suffix, MaxErrorsFor(probe)) >= 0)
            return false;

        return true;
    }

    /// <summary>
    /// BlackTextOnly / scrolled viewport: OCR no longer contains the typed suffix,
    /// so the whole fresh string is the next text to type.
    /// </summary>
    public static bool LooksLikeNewViewport(string known, string fresh)
    {
        if (string.IsNullOrEmpty(fresh))
            return false;
        if (string.IsNullOrEmpty(known))
            return true;
        return SharesNoOverlap(known, fresh);
    }

    public static bool SameStableCandidate(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal)) return true;
        if (a.Length < 4 || b.Length < 4) return false;

        var min = Math.Min(a.Length, b.Length);
        var same = 0;
        for (var i = 0; i < min; i++)
        {
            if (a[i] != b[i]) break;
            same++;
        }

        return same >= Math.Min(12, min);
    }

    private static int MaxErrorsFor(int length)
    {
        if (length < 8) return 0;
        if (length < 16) return 1;
        if (length < 32) return 2;
        if (length < 64) return 3;
        return 4;
    }

    private static int FindFuzzyLast(string haystack, string needle, int maxErrors)
    {
        if (maxErrors <= 0 || needle.Length == 0 || haystack.Length < needle.Length)
            return -1;

        for (var i = haystack.Length - needle.Length; i >= 0; i--)
        {
            var errors = 0;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] == needle[j])
                    continue;
                if (OCR.OcrTextCleanup.SameLetterIgnoreUmlaut(haystack[i + j], needle[j]))
                    continue;
                errors++;
                if (errors > maxErrors)
                    break;
            }

            if (errors <= maxErrors)
                return i;
        }

        return -1;
    }

    private static int LongestSuffixPrefix(string left, string right)
    {
        var max = Math.Min(left.Length, right.Length);
        for (var length = max; length > 0; length--)
        {
            if (left.AsSpan(left.Length - length, length).SequenceEqual(right.AsSpan(0, length)))
                return length;
        }

        return 0;
    }

    private static int LongestFuzzySuffixPrefix(string left, string right, int maxErrors)
    {
        if (maxErrors <= 0)
            return 0;

        var max = Math.Min(left.Length, right.Length);
        for (var length = max; length >= MinOverlap; length--)
        {
            var errors = 0;
            var leftStart = left.Length - length;
            for (var j = 0; j < length; j++)
            {
                if (left[leftStart + j] == right[j])
                    continue;
                if (OCR.OcrTextCleanup.SameLetterIgnoreUmlaut(left[leftStart + j], right[j]))
                    continue;
                errors++;
                if (errors > maxErrors)
                    break;
            }

            if (errors <= maxErrors)
                return length;
        }

        return 0;
    }
}
