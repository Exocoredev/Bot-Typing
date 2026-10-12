using System.Text;
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
            // Ctrl/Shift/F9 must be released before any synthetic key is sent.
            await _releaseGuard.WaitForHotkeyReleaseAsync(settings.StartHotkey, token);

            if (!await PrepareTargetAsync(targetWindow, settings, report, token))
            {
                report.StopReason = "Target window unavailable";
                return report;
            }

            if (settings.StartDelayMilliseconds > 0)
                await Task.Delay(settings.StartDelayMilliseconds, token);

            var keyboardLayout = _keyboard.GetKeyboardLayoutForWindow(targetWindow);

            // Live mode always begins with one silent fresh OCR read. No dialog is
            // opened, so the target window keeps focus. This prevents a stale OCR
            // preview from being typed after the page has already changed.
            if (liveReader is not null)
            {
                var freshStart = await liveReader(token);
                report.LiveOcrRefreshes++;
                knownText.Clear();
                if (!string.IsNullOrEmpty(freshStart))
                    knownText.Append(freshStart);
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
                await _releaseGuard.WaitForUserModifiersReleasedAsync(token);

                // Apply a background OCR result as soon as it is ready. Typing does
                // not wait for OCR while there are still buffered characters.
                if (refresh is not null && refresh.Task.IsCompleted)
                {
                    var fresh = await refresh.Task;
                    report.LiveOcrRefreshes++;
                    ApplyLiveRefresh(
                        refresh,
                        fresh,
                        knownText,
                        position,
                        report,
                        ref emptyScans,
                        ref stableCandidate,
                        ref stableCandidateCount);
                    refresh = null;
                    nextRefreshAllowedAt = Environment.TickCount64 + settings.LiveOcrPollIntervalMilliseconds;
                }

                var remaining = Math.Max(0, knownText.Length - position);

                // Start a non-blocking prefetch BEFORE the known text is exhausted.
                // The anchor lets us append only text that is genuinely new.
                if (liveReader is not null &&
                    refresh is null &&
                    remaining <= settings.LiveOcrPrefetchCharacters &&
                    Environment.TickCount64 >= nextRefreshAllowedAt)
                {
                    var anchor = remaining > 0 ? knownText.ToString(position, remaining) : string.Empty;
                    refresh = new LiveRefreshRequest(anchor, liveReader(token));
                }

                // No buffered character left: wait for Live OCR instead of declaring
                // the run finished immediately, which was the V5 bug.
                if (position >= knownText.Length)
                {
                    if (liveReader is null)
                        break;

                    if (refresh is null)
                    {
                        var wait = (int)Math.Max(0, nextRefreshAllowedAt - Environment.TickCount64);
                        if (wait > 0)
                            await Task.Delay(Math.Min(wait, settings.LiveOcrPollIntervalMilliseconds), token);

                        refresh = new LiveRefreshRequest(string.Empty, liveReader(token));
                    }

                    var fresh = await refresh.Task;
                    report.LiveOcrRefreshes++;
                    ApplyLiveRefresh(
                        refresh,
                        fresh,
                        knownText,
                        position,
                        report,
                        ref emptyScans,
                        ref stableCandidate,
                        ref stableCandidateCount);
                    refresh = null;
                    nextRefreshAllowedAt = Environment.TickCount64 + settings.LiveOcrPollIntervalMilliseconds;

                    if (position < knownText.Length)
                        continue;

                    if (emptyScans >= settings.LiveOcrEmptyScansToFinish)
                        break;

                    await Task.Delay(settings.LiveOcrPollIntervalMilliseconds, token);
                    continue;
                }

                if (!await PrepareTargetAsync(targetWindow, settings, report, token))
                {
                    report.StopReason = "Target window lost focus";
                    break;
                }

                var correct = knownText[position];
                var makeError = liveReader is null
                    ? false
                    : planner.ShouldMakeStreamingError(correct, position, settings, random, ref lastErrorIndex);

                // Static mode keeps its exact pre-planned percentage behavior.
                // Build the plan lazily only once when Live OCR is disabled.
                if (liveReader is null)
                {
                    // For the static path the old planner is exact and deterministic.
                    // It is created once and cached in this local variable below.
                    staticErrorPlan ??= planner.Plan(knownText.ToString(), settings, random);
                    makeError = staticErrorPlan.Contains(position);
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
            // Keep the report so AppController can still persist diagnostics.
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
            // Empty buffer: never append the full OCR blindly — diff against known text.
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

        if (!aligned && request.Anchor.Length > 0 && hadText)
            report.LiveOcrAlignmentMisses++;

        // While characters are still buffered, a scan with no extension simply
        // means that the UI has not revealed enough new text yet.
        if (position < knownText.Length)
            return;

        // Truly empty OCR → count toward finish.
        if (fresh.Length == 0)
        {
            emptyScans++;
            stableCandidate = string.Empty;
            stableCandidateCount = 0;
            return;
        }

        // Non-empty OCR without an addition: wait for a stable hard-scroll candidate.
        // Do NOT increment emptyScans while stabilizing — that caused early "Fertig".
        if (LiveTextMerger.SameStableCandidate(stableCandidate, fresh))
            stableCandidateCount++;
        else
        {
            stableCandidate = fresh;
            stableCandidateCount = 1;
        }

        if (stableCandidateCount < 2)
            return;

        if (knownText.Length == 0)
        {
            knownText.Append(fresh);
            report.LiveOcrAppendedCharacters += fresh.Length;
            report.TargetCharacters = knownText.Length;
            emptyScans = 0;
            stableCandidate = string.Empty;
            stableCandidateCount = 0;
            return;
        }

        // Hard scroll: OCR shows a new viewport with no overlap to known text.
        if (LiveTextMerger.SharesNoOverlap(knownText.ToString(), fresh))
        {
            knownText.Append(fresh);
            report.LiveOcrAppendedCharacters += fresh.Length;
            report.TargetCharacters = knownText.Length;
            emptyScans = 0;
            stableCandidate = string.Empty;
            stableCandidateCount = 0;
            return;
        }

        // Stable OCR still showing already-known content — treat as idle scan.
        emptyScans++;
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
