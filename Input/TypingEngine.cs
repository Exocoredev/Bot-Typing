using System.Text;
using AutoInputTester.Dom;
using AutoInputTester.Hotkeys;
using AutoInputTester.Models;
using AutoInputTester.Native;
using AutoInputTester.Target;

namespace AutoInputTester.Input;

public sealed class TypingEngine
{
    private readonly KeyboardSender _keyboard = new();
    private readonly HotkeyReleaseGuard _releaseGuard = new();
    private readonly TargetWindowService _targetService;
    private CancellationTokenSource? _cts;
    private volatile bool _paused;

    public bool IsRunning { get; private set; }
    public bool IsPaused => _paused;

    public event Action<int, int, int>? ProgressChanged;
    public event Action<string>? StatusChanged;

    public TypingEngine(TargetWindowService targetService)
    {
        _targetService = targetService;
    }

    public Task<TestRunReport> StartAsync(
        string text,
        AppSettings settings,
        IntPtr targetWindow,
        string targetTitle)
    {
        return RunAsync(text, settings, targetWindow, targetTitle, null);
    }

    public Task<TestRunReport> StartContinuousAsync(
        string initialText,
        AppSettings settings,
        IntPtr targetWindow,
        string targetTitle,
        Func<CancellationToken, Task<string>> liveReader)
    {
        return RunAsync(initialText, settings, targetWindow, targetTitle, liveReader);
    }

    private async Task<TestRunReport> RunAsync(
        string initialText,
        AppSettings settings,
        IntPtr targetWindow,
        string targetTitle,
        Func<CancellationToken, Task<string>>? liveReader)
    {
        Stop();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _paused = false;
        IsRunning = true;

        var seed = settings.RandomSeed == 0 ? Random.Shared.Next(1, int.MaxValue) : settings.RandomSeed;
        var random = new Random(seed);
        var planner = new ErrorPlanner();
        var timing = new TimingGenerator(settings, random);
        var knownText = new StringBuilder(initialText ?? string.Empty);
        var position = 0;
        var lastErrorIndex = -1;
        HashSet<int>? staticErrorPlan = null;

        // Only truly empty OCR scans may finish a live run. Never finish because
        // merge/alignment failed while text is still visible.
        var emptyScansToFinish = Math.Clamp(settings.LiveOcrEmptyScansToFinish, 15, 100);

        var report = new TestRunReport
        {
            StartedAt = DateTime.Now,
            TargetCharacters = knownText.Length,
            RequestedStrokesPer10Minutes = settings.StrokesPer10Minutes,
            Seed = seed,
            TargetWindowTitle = targetTitle
        };

        LiveRefreshRequest? refresh = null;
        var nextRefreshAllowedAt = 0L;
        var emptyScans = 0;
        var stableCandidate = string.Empty;
        var stableCandidateCount = 0;

        try
        {
            await _releaseGuard.WaitForHotkeyReleaseAsync(settings.StartHotkey, token);

            if (!await PrepareTargetAsync(targetWindow, settings, report, token))
            {
                report.StopReason = "Target window unavailable";
                return report;
            }

            if (settings.StartDelayMilliseconds > 0)
                await Task.Delay(settings.StartDelayMilliseconds, token);

            var keyboardLayout = _keyboard.GetKeyboardLayoutForWindow(targetWindow);

            if (liveReader is not null)
            {
                StatusChanged?.Invoke("Text Startlesung …");
                var startRead = await AwaitOcrAsync(liveReader, token);
                report.LiveOcrRefreshes++;
                knownText.Clear();
                if (!startRead.TimedOut && !string.IsNullOrEmpty(startRead.Text))
                {
                    var startText = startRead.Text;
                    LiveTextMarkers.TryUnmarkDomRemaining(ref startText);
                    knownText.Append(startText);
                }
                report.TargetCharacters = knownText.Length;
            }

            if (knownText.Length == 0 && liveReader is null)
            {
                report.StopReason = "No text";
                return report;
            }

            while (true)
            {
                token.ThrowIfCancellationRequested();
                await WaitWhilePausedAsync(token);
                // After L+R Shift (lock) or manual keys: wait briefly, then continue automatically.
                await _releaseGuard.WaitForUserModifiersReleasedAsync(token);

                if (refresh is not null && refresh.Task.IsCompleted)
                {
                    var read = await AwaitCompletedOcrAsync(refresh.Task, token);
                    report.LiveOcrRefreshes++;
                    if (!read.TimedOut)
                    {
                        ApplyLiveRefresh(
                            refresh,
                            read.Text,
                            knownText,
                            position,
                            report,
                            ref emptyScans,
                            ref stableCandidate,
                            ref stableCandidateCount);
                    }
                    refresh = null;
                    nextRefreshAllowedAt = Environment.TickCount64 + settings.LiveOcrPollIntervalMilliseconds;
                }

                var remaining = Math.Max(0, knownText.Length - position);

                if (liveReader is not null &&
                    refresh is null &&
                    remaining <= settings.LiveOcrPrefetchCharacters &&
                    Environment.TickCount64 >= nextRefreshAllowedAt)
                {
                    var anchor = remaining > 0
                        ? knownText.ToString(position, remaining)
                        : string.Empty;
                    refresh = new LiveRefreshRequest(anchor, liveReader(token));
                }

                if (position >= knownText.Length)
                {
                    if (liveReader is null)
                        break;

                    StatusChanged?.Invoke($"Warte auf neuen Text … (leere Scans {emptyScans}/{emptyScansToFinish})");

                    if (refresh is null)
                    {
                        var wait = (int)Math.Max(0, nextRefreshAllowedAt - Environment.TickCount64);
                        if (wait > 0)
                            await Task.Delay(Math.Min(wait, settings.LiveOcrPollIntervalMilliseconds), token);

                        // Empty buffer: no typed-suffix anchor. BlackTextOnly usually
                        // shows only the next untyped chunk — merge against known instead.
                        refresh = new LiveRefreshRequest(string.Empty, liveReader(token));
                    }

                    var read = await AwaitOcrTaskAsync(refresh.Task, token);
                    report.LiveOcrRefreshes++;
                    refresh = null;
                    nextRefreshAllowedAt = Environment.TickCount64 + settings.LiveOcrPollIntervalMilliseconds;

                    if (read.TimedOut)
                    {
                        StatusChanged?.Invoke("OCR langsam – warte weiter (zählt nicht als fertig) …");
                        await Task.Delay(settings.LiveOcrPollIntervalMilliseconds, token);
                        continue;
                    }

                    ApplyLiveRefresh(
                        new LiveRefreshRequest(string.Empty, Task.FromResult(read.Text)),
                        read.Text,
                        knownText,
                        position,
                        report,
                        ref emptyScans,
                        ref stableCandidate,
                        ref stableCandidateCount);

                    if (position < knownText.Length)
                        continue;

                    if (emptyScans >= emptyScansToFinish)
                    {
                        report.StopReason =
                            $"Completed: {emptyScans} wirklich leere OCR-Scans (kein sichtbarer Text mehr)";
                        break;
                    }

                    await Task.Delay(settings.LiveOcrPollIntervalMilliseconds, token);
                    continue;
                }

                if (!await PrepareTargetAsync(targetWindow, settings, report, token))
                {
                    report.StopReason = "Target window lost focus";
                    break;
                }

                var correct = knownText[position];
                var makeError = false;
                // Treat tiny float leftovers as zero — intentional typos must stay off.
                if (settings.ErrorPercent >= 0.05)
                {
                    makeError = liveReader is null
                        ? false
                        : planner.ShouldMakeStreamingError(correct, position, settings, random, ref lastErrorIndex);

                    if (liveReader is null)
                    {
                        staticErrorPlan ??= planner.Plan(knownText.ToString(), settings, random);
                        makeError = staticErrorPlan.Contains(position);
                    }
                }

                if (makeError)
                {
                    report.PlannedErrors++;
                    report.ErrorPositions.Add(position);

                    var wrong = planner.WrongCharacter(correct, random);
                    _keyboard.SendCharacter(wrong, keyboardLayout);
                    report.SentKeyEvents++;
                    ProgressChanged?.Invoke(position, knownText.Length, report.SentKeyEvents);
                    await Task.Delay(timing.NextDelayMilliseconds(wrong, afterError: true), token);

                    if (settings.CorrectionMode == CorrectionMode.Backspace)
                    {
                        await _releaseGuard.WaitForUserModifiersReleasedAsync(token);
                        if (!await PrepareTargetAsync(targetWindow, settings, report, token))
                        {
                            report.StopReason = "Target window lost focus";
                            break;
                        }

                        _keyboard.SendBackspace(keyboardLayout);
                        report.SentKeyEvents++;
                        await Task.Delay(Math.Max(20, timing.NextDelayMilliseconds('\b') / 2), token);
                        report.CorrectedErrors++;
                    }

                    await _releaseGuard.WaitForUserModifiersReleasedAsync(token);
                    if (!await PrepareTargetAsync(targetWindow, settings, report, token))
                    {
                        report.StopReason = "Target window lost focus";
                        break;
                    }

                    _keyboard.SendCharacter(correct, keyboardLayout);
                    report.SentKeyEvents++;
                    await Task.Delay(timing.NextDelayMilliseconds(correct), token);
                }
                else
                {
                    _keyboard.SendCharacter(correct, keyboardLayout);
                    report.SentKeyEvents++;
                    await Task.Delay(timing.NextDelayMilliseconds(correct), token);
                }

                position++;
                report.TypedCharacters = position;
                report.TargetCharacters = Math.Max(report.TargetCharacters, knownText.Length);
                emptyScans = 0;
                stableCandidate = string.Empty;
                stableCandidateCount = 0;

                ProgressChanged?.Invoke(position, knownText.Length, report.SentKeyEvents);
            }
        }
        catch (OperationCanceledException)
        {
            report.StopReason = "Stopped by user";
        }
        catch (Exception ex)
        {
            report.StopReason = "Error: " + ex.Message;
        }
        finally
        {
            report.TargetCharacters = Math.Max(report.TargetCharacters, knownText.Length);
            report.TypedCharacters = position;
            report.FinishedAt = DateTime.Now;
            IsRunning = false;
            _paused = false;
        }

        if (string.IsNullOrEmpty(report.StopReason))
            report.StopReason = "Completed";

        return report;
    }

    private readonly record struct OcrRead(string Text, bool TimedOut);

    private static async Task<OcrRead> AwaitOcrAsync(Func<CancellationToken, Task<string>> liveReader, CancellationToken token)
        => await AwaitOcrTaskAsync(liveReader(token), token);

    private static async Task<OcrRead> AwaitCompletedOcrAsync(Task<string> task, CancellationToken token)
        => await AwaitOcrTaskAsync(task, token);

    private static async Task<OcrRead> AwaitOcrTaskAsync(Task<string> task, CancellationToken token)
    {
        var delay = Task.Delay(4000, token);
        var finished = await Task.WhenAny(task, delay);
        if (finished != task)
            return new OcrRead(string.Empty, TimedOut: true);

        var text = await task;
        return new OcrRead(text ?? string.Empty, TimedOut: false);
    }

    private static void ApplyLiveRefresh(
        LiveRefreshRequest request,
        string fresh,
        StringBuilder knownText,
        int position,
        TestRunReport report,
        ref int emptyScans,
        ref string stableCandidate,
        ref int stableCandidateCount)
    {
        fresh ??= string.Empty;

        // Exact remaining text from browser DOM — replace untyped buffer, no OCR merge.
        if (LiveTextMarkers.TryUnmarkDomRemaining(ref fresh))
        {
            var remaining = Math.Max(0, knownText.Length - position);
            var previous = remaining > 0 ? knownText.ToString(position, remaining) : string.Empty;
            var changed = !string.Equals(previous, fresh, StringComparison.Ordinal);

            if (remaining > 0)
                knownText.Remove(position, remaining);

            if (fresh.Length > 0)
            {
                knownText.Append(fresh);
                report.LiveOcrAppendedCharacters += fresh.Length;
                report.TargetCharacters = knownText.Length;
                if (changed) report.LiveOcrResyncs++;
                emptyScans = 0;
                stableCandidate = string.Empty;
                stableCandidateCount = 0;
            }
            else if (position >= knownText.Length)
            {
                emptyScans++;
                stableCandidate = string.Empty;
                stableCandidateCount = 0;
            }

            return;
        }

        var addition = string.Empty;
        var aligned = false;
        var hadText = fresh.Length > 0;

        if (request.Anchor.Length > 0)
        {
            var merge = LiveTextMerger.Merge(request.Anchor, fresh);
            addition = merge.Addition;
            aligned = merge.Aligned;
            hadText = merge.HadText;
        }
        else if (fresh.Length > 0 && knownText.Length > 0)
        {
            addition = LiveTextMerger.NewTextAfterKnown(knownText.ToString(), fresh);
            aligned = addition.Length > 0
                      || knownText.ToString().Contains(fresh, StringComparison.Ordinal);
        }
        else if (fresh.Length > 0 && knownText.Length == 0)
        {
            aligned = false;
        }
        else
        {
            aligned = true;
        }

        if (addition.Length > 0)
        {
            knownText.Append(addition);
            report.LiveOcrAppendedCharacters += addition.Length;
            report.TargetCharacters = knownText.Length;
            emptyScans = 0;
            stableCandidate = string.Empty;
            stableCandidateCount = 0;
            return;
        }

        // Desync: replace untyped buffer only when OCR length is plausible.
        if (!aligned && request.Anchor.Length > 0 && hadText && position < knownText.Length)
        {
            report.LiveOcrAlignmentMisses++;
            var remaining = knownText.Length - position;
            if (remaining >= 3 && fresh.Length >= Math.Max(6, remaining / 2))
            {
                knownText.Remove(position, remaining);
                knownText.Append(fresh);
                report.LiveOcrResyncs++;
                report.LiveOcrAppendedCharacters += fresh.Length;
                report.TargetCharacters = knownText.Length;
                emptyScans = 0;
                stableCandidate = string.Empty;
                stableCandidateCount = 0;
            }

            return;
        }

        if (!aligned && request.Anchor.Length > 0 && hadText)
            report.LiveOcrAlignmentMisses++;

        if (position < knownText.Length)
            return;

        // ——— buffer empty ———

        if (fresh.Length == 0)
        {
            emptyScans++;
            stableCandidate = string.Empty;
            stableCandidateCount = 0;
            return;
        }

        if (LiveTextMerger.SameStableCandidate(stableCandidate, fresh))
            stableCandidateCount++;
        else
        {
            stableCandidate = fresh;
            stableCandidateCount = 1;
        }

        if (stableCandidateCount < 2)
            return;

        if (knownText.Length == 0 || LiveTextMerger.LooksLikeNewViewport(knownText.ToString(), fresh))
        {
            knownText.Append(fresh);
            report.LiveOcrAppendedCharacters += fresh.Length;
            report.TargetCharacters = knownText.Length;
            emptyScans = 0;
            stableCandidate = string.Empty;
            stableCandidateCount = 0;
            return;
        }

        stableCandidate = string.Empty;
        stableCandidateCount = 0;
    }

    public void TogglePause()
    {
        if (IsRunning) _paused = !_paused;
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
    }

    private async Task<bool> PrepareTargetAsync(IntPtr targetWindow, AppSettings settings, TestRunReport report, CancellationToken token)
    {
        if (!_targetService.IsAlive(targetWindow))
            return false;

        if (_targetService.IsForeground(targetWindow))
            return true;

        if (settings.FocusMode == FocusMode.Ignore)
            return true;

        if (settings.FocusMode == FocusMode.Stop)
            return false;

        var timeout = Math.Clamp(settings.FocusRestoreTimeoutMilliseconds, 100, 10000);
        var started = Environment.TickCount64;
        var counted = false;

        while (Environment.TickCount64 - started < timeout)
        {
            token.ThrowIfCancellationRequested();
            if (_targetService.EnsureForeground(targetWindow))
            {
                if (!counted)
                {
                    report.FocusRestores++;
                    counted = true;
                }
                await Task.Delay(35, token);
                return _targetService.IsForeground(targetWindow);
            }

            await Task.Delay(35, token);
        }

        return false;
    }

    private async Task WaitWhilePausedAsync(CancellationToken token)
    {
        while (_paused)
            await Task.Delay(40, token);
    }

    private sealed class LiveRefreshRequest
    {
        public string Anchor { get; }
        public Task<string> Task { get; }

        public LiveRefreshRequest(string anchor, Task<string> task)
        {
            Anchor = anchor;
            Task = task;
        }
    }
}
