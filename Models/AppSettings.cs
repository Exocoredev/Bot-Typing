namespace AutoInputTester.Models;

public enum OcrReadMode
{
    WholeSelection,
    RightOfCaret,
    BlackTextOnly
}

public enum CorrectionMode
{
    Backspace,
    NoBackspace
}

public enum FocusMode
{
    Restore,
    Stop,
    Ignore
}

public sealed class AppSettings
{
    public int SettingsVersion { get; set; } = 7;

    public int StrokesPer10Minutes { get; set; } = 2000;
    public double SpeedVariationPercent { get; set; } = 10.0;
    public int StartDelayMilliseconds { get; set; } = 100;

    public double ErrorPercent { get; set; } = 2.0;
    public int MinimumErrorGap { get; set; } = 3;
    public bool PreferEarlyErrors { get; set; } = true;
    public CorrectionMode CorrectionMode { get; set; } = CorrectionMode.NoBackspace;
    public bool ErrorsOnSpaces { get; set; } = false;

    public string OcrLanguageTag { get; set; } = "de-DE";
    public OcrReadMode OcrMode { get; set; } = OcrReadMode.BlackTextOnly;
    public bool KeepSpaces { get; set; } = true;
    public bool KeepLineBreaks { get; set; } = false;

    // V6: The selected screen area can be re-read while typing. This is what
    // allows horizontally scrolling / newly appearing text to be appended.
    public bool LiveOcrEnabled { get; set; } = true;
    public int LiveOcrPrefetchCharacters { get; set; } = 45;
    public int LiveOcrPollIntervalMilliseconds { get; set; } = 220;
    public int LiveOcrEmptyScansToFinish { get; set; } = 25;

    // V7: Prefer exact exercise text from the browser DOM (Chrome/Edge CDP)
    // over OCR. Needs --remote-debugging-port=9222 (starten-chrome-typewriter.bat).
    public bool UseDomTextSource { get; set; } = true;
    public int DomDebugPort { get; set; } = 9222;

    public string SelectHotkey { get; set; } = "Ctrl+Shift+S";
    public string StartHotkey { get; set; } = "Ctrl+Shift+F9";
    public string PauseHotkey { get; set; } = "Ctrl+Shift+F10";
    // Ctrl+Alt+Escape is often swallowed by other hooks; F11 is reliable.
    public string StopHotkey { get; set; } = "Ctrl+Shift+F11";

    public FocusMode FocusMode { get; set; } = FocusMode.Restore;
    public int FocusRestoreTimeoutMilliseconds { get; set; } = 1500;

    // Kept for compatibility with older settings. In Live OCR mode V6 always
    // performs a silent fresh read at the start, without opening any window.
    public bool RefreshOcrOnStart { get; set; } = false;
    public int RandomSeed { get; set; } = 0;

    // UI / notifications — each can be disabled individually.
    public bool ShowTrayBalloons { get; set; } = true;
    public bool ShowOcrPreviewWindow { get; set; } = true;
    public bool ShowErrorDialogs { get; set; } = true;
    public bool ShowFinishBalloon { get; set; } = true;
}
