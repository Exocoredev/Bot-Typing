using AutoInputTester.Models;

namespace AutoInputTester.Input;

public sealed class TimingGenerator
{
    private readonly AppSettings _settings;
    private readonly Random _random;
    private double _phaseFactor = 1.0;
    private int _phaseRemaining;

    public TimingGenerator(AppSettings settings, Random random)
    {
        _settings = settings;
        _random = random;
    }

    public int NextDelayMilliseconds(char ch, bool afterError = false)
    {
        if (_phaseRemaining <= 0)
        {
            var max = Math.Clamp(_settings.SpeedVariationPercent / 100.0, 0, 0.8);
            _phaseFactor = 1.0 + ((_random.NextDouble() * 2.0 - 1.0) * max * 0.65);
            _phaseRemaining = _random.Next(8, 28);
        }
        _phaseRemaining--;

        var baseDelay = 600000.0 / Math.Max(10, _settings.StrokesPer10Minutes);
        var variation = Math.Clamp(_settings.SpeedVariationPercent / 100.0, 0, 0.8);
        var perKey = 1.0 + ((_random.NextDouble() * 2.0 - 1.0) * variation * 0.35);
        var delay = baseDelay * _phaseFactor * perKey;

        if (ch is '.' or '!' or '?') delay += baseDelay * 0.9;
        else if (ch is ',' or ';' or ':') delay += baseDelay * 0.35;
        else if (ch == '\n') delay += baseDelay * 0.7;

        if (afterError) delay += baseDelay * (0.45 + _random.NextDouble() * 0.5);

        return Math.Max(1, (int)Math.Round(delay));
    }
}
