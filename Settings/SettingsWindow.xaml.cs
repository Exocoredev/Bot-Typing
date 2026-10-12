using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AutoInputTester.Hotkeys;
using AutoInputTester.Models;

namespace AutoInputTester.Settings;

public partial class SettingsWindow : Window
{
    public AppSettings Result { get; private set; }

    public SettingsWindow(AppSettings source)
    {
        InitializeComponent();
        Result = Clone(source);
        LoadToControls(Result);
    }

    private void LoadToControls(AppSettings s)
    {
        StrokesBox.Text = s.StrokesPer10Minutes.ToString(CultureInfo.InvariantCulture);
        VariationBox.Text = s.SpeedVariationPercent.ToString(CultureInfo.InvariantCulture);
        StartDelayBox.Text = s.StartDelayMilliseconds.ToString(CultureInfo.InvariantCulture);
        ErrorBox.Text = s.ErrorPercent.ToString(CultureInfo.InvariantCulture);
        GapBox.Text = s.MinimumErrorGap.ToString(CultureInfo.InvariantCulture);
        EarlyErrorsCheck.IsChecked = s.PreferEarlyErrors;
        ErrorsOnSpacesCheck.IsChecked = s.ErrorsOnSpaces;
        LanguageBox.Text = s.OcrLanguageTag;
        SpacesCheck.IsChecked = s.KeepSpaces;
        LineBreaksCheck.IsChecked = s.KeepLineBreaks;
        LiveOcrCheck.IsChecked = s.LiveOcrEnabled;
        LivePrefetchBox.Text = s.LiveOcrPrefetchCharacters.ToString(CultureInfo.InvariantCulture);
        LivePollBox.Text = s.LiveOcrPollIntervalMilliseconds.ToString(CultureInfo.InvariantCulture);
        LiveEmptyScansBox.Text = s.LiveOcrEmptyScansToFinish.ToString(CultureInfo.InvariantCulture);
        SelectHotkeyBox.Text = s.SelectHotkey;
        StartHotkeyBox.Text = s.StartHotkey;
        PauseHotkeyBox.Text = s.PauseHotkey;
        StopHotkeyBox.Text = s.StopHotkey;
        SeedBox.Text = s.RandomSeed.ToString(CultureInfo.InvariantCulture);
        FocusTimeoutBox.Text = s.FocusRestoreTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture);

        SelectCombo(CorrectionCombo, s.CorrectionMode.ToString());
        SelectCombo(OcrModeCombo, s.OcrMode.ToString());
        SelectCombo(FocusModeCombo, s.FocusMode.ToString());
    }

    private static void SelectCombo(System.Windows.Controls.ComboBox box, string tag)
    {
        foreach (var item in box.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = item;
                return;
            }
        }
        box.SelectedIndex = 0;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(StrokesBox.Text, out var strokes) || strokes < 10 || strokes > 100000)
        {
            System.Windows.MessageBox.Show("Ungültige Anschläge/10 Minuten."); return;
        }
        if (!double.TryParse(VariationBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var variation) || variation < 0 || variation > 80)
        {
            System.Windows.MessageBox.Show("Ungültige Geschwindigkeitsschwankung."); return;
        }
        if (!int.TryParse(StartDelayBox.Text, out var startDelay) || startDelay < 0 || startDelay > 60000)
        {
            System.Windows.MessageBox.Show("Ungültige Startverzögerung."); return;
        }
        if (!double.TryParse(ErrorBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var error) || error < 0 || error > 50)
        {
            System.Windows.MessageBox.Show("Ungültige Fehlerquote."); return;
        }
        if (!int.TryParse(GapBox.Text, out var gap) || gap < 1 || gap > 1000)
        {
            System.Windows.MessageBox.Show("Ungültiger Mindestabstand."); return;
        }
        if (!int.TryParse(LivePrefetchBox.Text, out var livePrefetch) || livePrefetch < 5 || livePrefetch > 500)
        {
            System.Windows.MessageBox.Show("Live-OCR Restpuffer muss zwischen 5 und 500 Zeichen liegen."); return;
        }
        if (!int.TryParse(LivePollBox.Text, out var livePoll) || livePoll < 80 || livePoll > 5000)
        {
            System.Windows.MessageBox.Show("Live-OCR Intervall muss zwischen 80 und 5000 ms liegen."); return;
        }
        if (!int.TryParse(LiveEmptyScansBox.Text, out var emptyScans) || emptyScans < 2 || emptyScans > 100)
        {
            System.Windows.MessageBox.Show("Leere Live-OCR Scans müssen zwischen 2 und 100 liegen."); return;
        }
        if (!int.TryParse(SeedBox.Text, out var seed) || seed < 0)
        {
            System.Windows.MessageBox.Show("Ungültiger Random Seed."); return;
        }
        if (!int.TryParse(FocusTimeoutBox.Text, out var focusTimeout) || focusTimeout < 100 || focusTimeout > 10000)
        {
            System.Windows.MessageBox.Show("Fokus-Timeout muss zwischen 100 und 10000 ms liegen."); return;
        }

        var selectHotkey = SelectHotkeyBox.Text.Trim();
        var startHotkey = StartHotkeyBox.Text.Trim();
        var pauseHotkey = PauseHotkeyBox.Text.Trim();
        var stopHotkey = StopHotkeyBox.Text.Trim();
        try
        {
            GlobalHotkeyService.ValidateHotkeys(selectHotkey, startHotkey, pauseHotkey, stopHotkey);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Hotkey ungültig");
            return;
        }

        Result.SettingsVersion = 6;
        Result.StrokesPer10Minutes = strokes;
        Result.SpeedVariationPercent = variation;
        Result.StartDelayMilliseconds = startDelay;
        Result.ErrorPercent = error;
        Result.MinimumErrorGap = gap;
        Result.PreferEarlyErrors = EarlyErrorsCheck.IsChecked == true;
        Result.ErrorsOnSpaces = ErrorsOnSpacesCheck.IsChecked == true;
        Result.OcrLanguageTag = string.IsNullOrWhiteSpace(LanguageBox.Text) ? "de-DE" : LanguageBox.Text.Trim();
        Result.KeepSpaces = SpacesCheck.IsChecked == true;
        Result.KeepLineBreaks = LineBreaksCheck.IsChecked == true;
        Result.LiveOcrEnabled = LiveOcrCheck.IsChecked == true;
        Result.LiveOcrPrefetchCharacters = livePrefetch;
        Result.LiveOcrPollIntervalMilliseconds = livePoll;
        Result.LiveOcrEmptyScansToFinish = emptyScans;
        Result.SelectHotkey = selectHotkey;
        Result.StartHotkey = startHotkey;
        Result.PauseHotkey = pauseHotkey;
        Result.StopHotkey = stopHotkey;
        Result.RandomSeed = seed;
        Result.FocusRestoreTimeoutMilliseconds = focusTimeout;
        Result.RefreshOcrOnStart = false;
        Result.CorrectionMode = Enum.Parse<CorrectionMode>(((ComboBoxItem)CorrectionCombo.SelectedItem).Tag!.ToString()!);
        Result.OcrMode = Enum.Parse<OcrReadMode>(((ComboBoxItem)OcrModeCombo.SelectedItem).Tag!.ToString()!);
        Result.FocusMode = Enum.Parse<FocusMode>(((ComboBoxItem)FocusModeCombo.SelectedItem).Tag!.ToString()!);

        DialogResult = true;
    }

    private static AppSettings Clone(AppSettings s) => new()
    {
        SettingsVersion = 6,
        StrokesPer10Minutes = s.StrokesPer10Minutes,
        SpeedVariationPercent = s.SpeedVariationPercent,
        StartDelayMilliseconds = s.StartDelayMilliseconds,
        ErrorPercent = s.ErrorPercent,
        MinimumErrorGap = s.MinimumErrorGap,
        PreferEarlyErrors = s.PreferEarlyErrors,
        CorrectionMode = s.CorrectionMode,
        ErrorsOnSpaces = s.ErrorsOnSpaces,
        OcrLanguageTag = s.OcrLanguageTag,
        OcrMode = s.OcrMode,
        KeepSpaces = s.KeepSpaces,
        KeepLineBreaks = s.KeepLineBreaks,
        LiveOcrEnabled = s.LiveOcrEnabled,
        LiveOcrPrefetchCharacters = s.LiveOcrPrefetchCharacters,
        LiveOcrPollIntervalMilliseconds = s.LiveOcrPollIntervalMilliseconds,
        LiveOcrEmptyScansToFinish = s.LiveOcrEmptyScansToFinish,
        SelectHotkey = s.SelectHotkey,
        StartHotkey = s.StartHotkey,
        PauseHotkey = s.PauseHotkey,
        StopHotkey = s.StopHotkey,
        FocusMode = s.FocusMode,
        FocusRestoreTimeoutMilliseconds = s.FocusRestoreTimeoutMilliseconds,
        RefreshOcrOnStart = false,
        RandomSeed = s.RandomSeed
    };
}
