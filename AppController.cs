using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using AutoInputTester.Capture;
using AutoInputTester.Hotkeys;
using AutoInputTester.Input;
using AutoInputTester.Logging;
using AutoInputTester.Models;
using AutoInputTester.OCR;
using AutoInputTester.Overlay;
using AutoInputTester.Settings;
using AutoInputTester.Target;
using AutoInputTester.Tray;
using AutoInputTester.Windows;

namespace AutoInputTester;

public sealed class AppController : IDisposable
{
    private readonly SettingsService _settingsService = new();
    private readonly ScreenCaptureService _capture = new();
    private readonly OcrService _ocr = new();
    private readonly TargetWindowService _target = new();
    private readonly TestLogger _logger = new();

    private AppSettings _settings = new();
    private TrayManager? _tray;
    private GlobalHotkeyService? _hotkeys;
    private TypingEngine? _typing;
    private string _recognizedText = string.Empty;
    private Rectangle? _selectedRectangle;
    private bool _busySelecting;
    private IntPtr _lastTargetWindow = IntPtr.Zero;

    public void Start()
    {
        _settings = _settingsService.Load();
        _tray = new TrayManager();
        _typing = new TypingEngine(_target);
        _typing.ProgressChanged += OnProgress;

        _tray.SelectAreaRequested += () => RunAsyncOnUi(SelectAreaAsync);
        _tray.PreviewRequested += () => RunOnUi(ShowPreview);
        _tray.StartRequested += () => RunAsyncOnUi(StartFromTrayAsync);
        _tray.PauseRequested += () => RunOnUi(TogglePause);
        _tray.StopRequested += () => RunOnUi(StopTyping);
        _tray.SettingsRequested += () => RunOnUi(ShowSettings);
        _tray.LogsRequested += () => RunOnUi(OpenLogs);
        _tray.ExitRequested += () => RunOnUi(Exit);

        _hotkeys = new GlobalHotkeyService();
        _hotkeys.Pressed += action =>
        {
            switch (action)
            {
                case HotkeyAction.SelectArea:
                    RunAsyncOnUi(SelectAreaAsync);
                    break;

                case HotkeyAction.Start:
                    // Capture the target BEFORE doing anything else. No dialog,
                    // tray action or async OCR gets a chance to steal the target.
                    var targetWindow = _target.CaptureForegroundWindow();
                    if (targetWindow != IntPtr.Zero)
                        _lastTargetWindow = targetWindow;
                    RunAsyncOnUi(() => StartTypingAsync(targetWindow));
                    break;

                case HotkeyAction.Pause:
                    RunOnUi(TogglePause);
                    break;

                case HotkeyAction.Stop:
                    RunOnUi(StopTyping);
                    break;
            }
        };

        RegisterHotkeys();
        _tray.SetStatus("Bereit – Bereich auswählen");
    }

    private void RegisterHotkeys()
    {
        try
        {
            _hotkeys!.RegisterDefaults(_settings.SelectHotkey, _settings.StartHotkey, _settings.PauseHotkey, _settings.StopHotkey);
        }
        catch (Exception ex)
        {
            _tray?.ShowBalloon("Hotkey-Fehler", ex.Message, ToolTipIcon.Warning);
        }
    }

    private async Task SelectAreaAsync()
    {
        if (_busySelecting || (_typing?.IsRunning ?? false)) return;
        _busySelecting = true;

        try
        {
            _tray?.SetStatus("Bereich auswählen");
            var overlay = new SelectionWindow();
            var accepted = overlay.ShowDialog() == true;
            if (!accepted || overlay.SelectedScreenRectangle is not Rectangle rectangle)
            {
                _tray?.SetStatus("Auswahl abgebrochen");
                return;
            }

            _selectedRectangle = rectangle;
            _tray?.SetStatus("OCR läuft …");
            await Task.Delay(100);

            using var screenshot = _capture.Capture(rectangle);
            using var prepared = ImagePreprocessor.Prepare(screenshot, _settings.OcrMode);
            var text = await _ocr.RecognizeAsync(prepared, _settings.OcrLanguageTag);
            text = NormalizeRecognizedText(text);

            var preview = new PreviewWindow(text);
            if (preview.ShowDialog() == true)
            {
                _recognizedText = NormalizeRecognizedText(preview.ResultText);
                _tray?.SetStatus($"Bereit – {_recognizedText.Length} Zeichen");
                _tray?.ShowBalloon(
                    "OCR übernommen",
                    $"{_recognizedText.Length} Zeichen. Jetzt das Zielfenster anklicken und {_settings.StartHotkey} drücken.");
            }
            else
            {
                _tray?.SetStatus("OCR-Vorschau verworfen");
            }
        }
        catch (Exception ex)
        {
            _tray?.SetStatus("OCR-Fehler");
            System.Windows.MessageBox.Show(ex.Message, "Auto Input Tester", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _busySelecting = false;
        }
    }

    private void ShowPreview()
    {
        if (string.IsNullOrEmpty(_recognizedText))
        {
            _tray?.ShowBalloon("Keine OCR-Daten", "Zuerst einen Bereich auswählen.", ToolTipIcon.Warning);
            return;
        }

        var preview = new PreviewWindow(_recognizedText);
        if (preview.ShowDialog() == true)
        {
            _recognizedText = NormalizeRecognizedText(preview.ResultText);
            _tray?.SetStatus($"Bereit – {_recognizedText.Length} Zeichen");
        }
    }

    private async Task StartFromTrayAsync()
    {
        if (_lastTargetWindow == IntPtr.Zero || !_target.IsAlive(_lastTargetWindow))
        {
            _tray?.ShowBalloon(
                "Kein Zielfenster gespeichert",
                $"Für einen sicheren Start zuerst das Zielfenster aktivieren und {_settings.StartHotkey} benutzen.",
                ToolTipIcon.Warning);
            return;
        }

        await StartTypingAsync(_lastTargetWindow);
    }

    private async Task StartTypingAsync(IntPtr targetWindow)
    {
        if (_typing is null || _typing.IsRunning) return;

        if (targetWindow == IntPtr.Zero || !_target.IsAlive(targetWindow))
        {
            _tray?.ShowBalloon("Kein Zielfenster", "Das Zielfenster konnte nicht erfasst werden.", ToolTipIcon.Warning);
            return;
        }

        _lastTargetWindow = targetWindow;
        var title = _target.GetWindowTitle(targetWindow);

        try
        {
            TestRunReport report;

            if (_settings.LiveOcrEnabled)
            {
                if (_selectedRectangle is not Rectangle liveRectangle)
                {
                    _tray?.ShowBalloon("Kein Bereich", "Für Live OCR zuerst einen Bildschirmbereich auswählen.", ToolTipIcon.Warning);
                    return;
                }

                _tray?.SetStatus($"Live-Start: {title}");
                report = await _typing.StartContinuousAsync(
                    _recognizedText,
                    _settings,
                    targetWindow,
                    title,
                    token => ReadLiveOcrAsync(liveRectangle, token));
            }
            else
            {
                // Static mode remains available for fixed text fields.
                if (_settings.RefreshOcrOnStart)
                {
                    if (_selectedRectangle is not Rectangle rectangle)
                    {
                        _tray?.ShowBalloon("Kein Bereich", "Zuerst einen Bildschirmbereich auswählen.", ToolTipIcon.Warning);
                        return;
                    }

                    _tray?.SetStatus("OCR aktualisieren …");
                    using var screenshot = _capture.Capture(rectangle);
                    using var prepared = ImagePreprocessor.Prepare(screenshot, _settings.OcrMode);
                    var freshText = await _ocr.RecognizeAsync(prepared, _settings.OcrLanguageTag);
                    freshText = NormalizeRecognizedText(freshText);

                    if (string.IsNullOrEmpty(freshText))
                    {
                        _tray?.ShowBalloon("OCR leer", "Im gespeicherten Bereich wurde kein Text erkannt.", ToolTipIcon.Warning);
                        return;
                    }

                    _recognizedText = freshText;
                }

                if (string.IsNullOrEmpty(_recognizedText))
                {
                    _tray?.ShowBalloon("Kein Text", "Zuerst einen Bereich auswählen und OCR übernehmen.", ToolTipIcon.Warning);
                    return;
                }

                _tray?.SetStatus($"Start: {title}");
                report = await _typing.StartAsync(_recognizedText, _settings, targetWindow, title);
            }

            _logger.Save(report);

            _tray?.SetStatus(report.StopReason == "Completed" ? "Fertig" : report.StopReason);
            _tray?.ShowBalloon(
                "Testlauf beendet",
                $"{report.StopReason}\n{report.TypedCharacters} Zeichen · {report.SentKeyEvents} Tasten · Live-OCR: {report.LiveOcrRefreshes}");
        }
        catch (Exception ex)
        {
            _tray?.SetStatus("Fehler");
            _tray?.ShowBalloon("Eingabefehler", ex.Message, ToolTipIcon.Error);
        }
    }

    private Task<string> ReadLiveOcrAsync(Rectangle rectangle, CancellationToken token)
    {
        // Run capture + preprocessing + OCR away from the UI/typing continuation.
        // No window is shown and the browser keeps focus.
        return Task.Run(async () =>
        {
            token.ThrowIfCancellationRequested();
            using var screenshot = _capture.Capture(rectangle);
            using var prepared = ImagePreprocessor.Prepare(screenshot, _settings.OcrMode);
            var text = await _ocr.RecognizeAsync(prepared, _settings.OcrLanguageTag);
            token.ThrowIfCancellationRequested();
            return NormalizeRecognizedText(text);
        }, token);
    }

    private string NormalizeRecognizedText(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');

        if (!_settings.KeepLineBreaks)
            text = text.Replace("\n", " ");
        if (!_settings.KeepSpaces)
            text = text.Replace(" ", "");

        return text;
    }

    private void TogglePause()
    {
        if (_typing is null || !_typing.IsRunning) return;
        _typing.TogglePause();
        _tray?.SetStatus(_typing.IsPaused ? "Pausiert" : "Fortgesetzt");
    }

    private void StopTyping()
    {
        _typing?.Stop();
        _tray?.SetStatus("Gestoppt");
    }

    private void OnProgress(int current, int total, int sent)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            _tray?.SetStatus($"Tippt: {current} / {total} erkannt – {sent} Tasten"));
    }

    private void ShowSettings()
    {
        if (_typing?.IsRunning == true) return;
        var window = new SettingsWindow(_settings);
        if (window.ShowDialog() != true) return;

        var previous = _settings;
        _settings = window.Result;
        try
        {
            RegisterHotkeysStrict();
            _settingsService.Save(_settings);
            _tray?.SetStatus("Einstellungen gespeichert");
        }
        catch (Exception ex)
        {
            _settings = previous;
            try { RegisterHotkeys(); } catch { }
            _tray?.ShowBalloon("Hotkey-Fehler", ex.Message, ToolTipIcon.Warning);
            _tray?.SetStatus("Einstellungen nicht übernommen");
        }
    }

    private void RegisterHotkeysStrict()
    {
        _hotkeys!.RegisterDefaults(_settings.SelectHotkey, _settings.StartHotkey, _settings.PauseHotkey, _settings.StopHotkey);
    }

    private void OpenLogs()
    {
        try
        {
            Directory.CreateDirectory(_logger.LogDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", _logger.LogDirectory) { UseShellExecute = true });
        }
        catch { }
    }

    private void Exit()
    {
        _typing?.Stop();
        System.Windows.Application.Current.Shutdown();
    }

    private static void RunOnUi(Action action)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(action);
    }

    private static void RunAsyncOnUi(Func<Task> action)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(async () => await action());
    }

    public void Dispose()
    {
        _typing?.Stop();
        _hotkeys?.Dispose();
        _tray?.Dispose();
    }
}
