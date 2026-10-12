using AutoInputTester.Models;

namespace AutoInputTester.Input;

public sealed class ErrorPlanner
{
    public HashSet<int> Plan(string text, AppSettings settings, Random random)
    {
        var eligible = Enumerable.Range(0, text.Length)
            .Where(i => IsEligible(text[i], settings))
            .ToList();

        var desired = (int)Math.Round(eligible.Count * settings.ErrorPercent / 100.0);
        if (desired <= 0) return new HashSet<int>();

        var ranked = eligible
            .Select(i => new
            {
                Index = i,
                Rank = WeightedRank(i, Math.Max(1, text.Length), settings.PreferEarlyErrors, random)
            })
            .OrderBy(x => x.Rank)
            .ToList();

        var selected = new List<int>();
        foreach (var candidate in ranked)
        {
            if (selected.All(existing => Math.Abs(existing - candidate.Index) > settings.MinimumErrorGap))
            {
                selected.Add(candidate.Index);
                if (selected.Count >= desired) break;
            }
        }

        return selected.ToHashSet();
    }


    public bool ShouldMakeStreamingError(char correct, int index, AppSettings settings, Random random, ref int lastErrorIndex)
    {
        if (settings.ErrorPercent <= 0 || !IsEligible(correct, settings))
            return false;

        if (lastErrorIndex >= 0 && index - lastErrorIndex <= settings.MinimumErrorGap)
            return false;

        var probability = Math.Clamp(settings.ErrorPercent / 100.0, 0.0, 0.5);

        // The total stream length is unknown in Live OCR mode. To keep the old
        // "errors rather early" behavior, slightly raise the probability near
        // the beginning and let it settle to the requested rate afterwards.
        if (settings.PreferEarlyErrors && index < 160)
        {
            var factor = 1.35 - (0.35 * index / 160.0);
            probability = Math.Min(0.75, probability * factor);
        }

        if (random.NextDouble() >= probability)
            return false;

        lastErrorIndex = index;
        return true;
    }

    public char WrongCharacter(char correct, Random random)
    {
        if (char.IsLetter(correct))
        {
            var lower = char.ToLowerInvariant(correct);
            var neighbors = NeighborCharacters(lower);
            var chosen = neighbors[random.Next(neighbors.Length)];
            return char.IsUpper(correct) ? char.ToUpperInvariant(chosen) : chosen;
        }

        if (char.IsDigit(correct))
        {
            const string digits = "0123456789";
            var idx = digits.IndexOf(correct);
            if (idx >= 0)
            {
                var alternatives = new List<char>();
                if (idx > 0) alternatives.Add(digits[idx - 1]);
                if (idx < digits.Length - 1) alternatives.Add(digits[idx + 1]);
                if (alternatives.Count > 0)
                    return alternatives[random.Next(alternatives.Count)];
            }
        }

        const string fallback = "asdfghjklö";
        return fallback[random.Next(fallback.Length)];
    }

    private static bool IsEligible(char c, AppSettings settings)
    {
        if (c is '\r' or '\n' or '\t') return false;
        if (char.IsWhiteSpace(c) && !settings.ErrorsOnSpaces) return false;
        return true;
    }

    private static double WeightedRank(int index, int length, bool preferEarly, Random random)
    {
        var u = Math.Max(1e-9, random.NextDouble());
        if (!preferEarly) return u;

        var position = index / (double)Math.Max(1, length - 1);
        var weight = 2.0 - 1.25 * position; // earlier positions get a higher selection weight
        return -Math.Log(u) / Math.Max(0.1, weight);
    }

    private static char[] NeighborCharacters(char c)
    {
        var map = new Dictionary<char, string>
        {
            ['q']="wa", ['w']="qeas", ['e']="wrsd", ['r']="etdf", ['t']="rzfg", ['z']="tugh",
            ['u']="zihj", ['i']="uokj", ['o']="ipkl", ['p']="oülö", ['ü']="pö",
            ['a']="qws y".Replace(" ",""), ['s']="awedx", ['d']="serfc", ['f']="drtgv", ['g']="ftzhb",
            ['h']="gzujn", ['j']="huikm", ['k']="jiol", ['l']="kopö", ['ö']="lpä", ['ä']="ö",
            ['y']="asx", ['x']="ysdc", ['c']="xdfv", ['v']="cfgb", ['b']="vghn", ['n']="bhjm", ['m']="njk"
        };

        if (map.TryGetValue(c, out var s) && s.Length > 0)
            return s.ToCharArray();
        return "asdfghjkl".ToCharArray();
    }
}
