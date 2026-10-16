using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using AutoInputTester.Capture;
using AutoInputTester.Dom;
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
    private readonly ChromeDevToolsTextReader _domReader = new();
    private readonly TargetWindowService _target = new();
    private readonly TestLogger _logger = new();
    private bool _domSourceActive;

    private AppSettings _settings = new();
    private TrayManager? _tray;
    private GlobalHotkeyService? _hotkeys;
    private KeyboardLockService? _keyboardLock;
    private TypingEngine? _typing;
    private string _recognizedText = string.Empty;
    private Rectangle? _selectedRectangle;
    private bool _busySelecting;
    private IntPtr _lastTargetWindow = IntPtr.Zero;
    private int _consensusDisagreements;
    private int _consensusDroppedCharacters;

    public void Start()
    {
        _settings = _settingsService.Load();
        _tray = new TrayManager();
        _typing = new TypingEngine(_target);
        _typing.ProgressChanged += OnProgress;
        _typing.StatusChanged += OnStatus;

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

        _keyboardLock = new KeyboardLockService();
        _keyboardLock.LockChanged += locked => RunOnUi(() => OnKeyboardLockChanged(locked));
        try
        {
            _keyboardLock.Start();
        }
        catch (Exception ex)
        {
            NotifyBalloon("Tastatursperre", ex.Message, ToolTipIcon.Warning);
        }

        RegisterHotkeys();
        _tray.SetStatus("Bereit – Bereich auswählen");
    }

    private void OnKeyboardLockChanged(bool locked)
    {
        _tray?.SetKeyboardLocked(locked);
        if (locked)
        {
            _tray?.SetStatus("Kamera AN – Tastatur gesperrt");
            NotifyBalloon("Tastatursperre", "Ein. Entsperren: a s d f j k l ö + - gleichzeitig (oder erneut L-Shift+R-Shift).", ToolTipIcon.Info);
        }
        else
        {
            _tray?.SetStatus("Bereit – Tastatur entsperrt");
            NotifyBalloon("Tastatursperre", "Aus.", ToolTipIcon.Info);
        }
    }

    private void NotifyBalloon(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        if (!_settings.ShowTrayBalloons) return;
        _tray?.ShowBalloon(title, message, icon);
    }

    private void NotifyFinishBalloon(string title, string message)
    {
        if (!_settings.ShowTrayBalloons || !_settings.ShowFinishBalloon) return;
        _tray?.ShowBalloon(title, message);
    }

    private void NotifyError(string message)
    {
        if (_settings.ShowErrorDialogs)
            System.Windows.MessageBox.Show(message, "Auto Input Tester", MessageBoxButton.OK, MessageBoxImage.Error);
        else
            NotifyBalloon("Fehler", message, ToolTipIcon.Error);
    }

    private void RegisterHotkeys()
    {
        try
        {
            _hotkeys!.RegisterDefaults(_settings.SelectHotkey, _settings.StartHotkey, _settings.PauseHotkey, _settings.StopHotkey);
        }
        catch (Exception ex)
        {
            NotifyBalloon("Hotkey-Fehler", ex.Message, ToolTipIcon.Warning);
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

            var text = await CaptureOcrConsensusAsync(rectangle, CancellationToken.None);

            if (_settings.ShowOcrPreviewWindow)
            {
                var preview = new PreviewWindow(text);
                if (preview.ShowDialog() == true)
                {
                    _recognizedText = NormalizeRecognizedText(preview.ResultText);
                    _tray?.SetStatus($"Bereit – {_recognizedText.Length} Zeichen");
                    NotifyBalloon(
                        "OCR übernommen",
                        $"{_recognizedText.Length} Zeichen. Jetzt das Zielfenster anklicken und {_settings.StartHotkey} drücken.");
                }
                else
                {
                    _tray?.SetStatus("OCR-Vorschau verworfen");
                }
            }
            else
            {
                _recognizedText = NormalizeRecognizedText(text);
                _tray?.SetStatus($"Bereit – {_recognizedText.Length} Zeichen");
                NotifyBalloon(
                    "OCR übernommen",
                    $"{_recognizedText.Length} Zeichen (ohne Vorschau). {_settings.StartHotkey} zum Start.");
            }
        }
        catch (Exception ex)
        {
            _tray?.SetStatus("OCR-Fehler");
            NotifyError(ex.Message);
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
            NotifyBalloon("Keine OCR-Daten", "Zuerst einen Bereich auswählen.", ToolTipIcon.Warning);
            return;
        }

        if (!_settings.ShowOcrPreviewWindow)
        {
            _tray?.SetStatus($"Bereit – {_recognizedText.Length} Zeichen");
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
            NotifyBalloon(
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
            NotifyBalloon("Kein Zielfenster", "Das Zielfenster konnte nicht erfasst werden.", ToolTipIcon.Warning);
            return;
        }

        _lastTargetWindow = targetWindow;
        var title = _target.GetWindowTitle(targetWindow);

        try
        {
            TestRunReport report;
            _consensusDisagreements = 0;
            _consensusDroppedCharacters = 0;

            if (_settings.LiveOcrEnabled)
            {
                _domSourceActive = false;
                if (_settings.UseDomTextSource)
                {
                    var domProbe = await _domReader.TryReadTypewriterRemainingAsync(CancellationToken.None);
                    if (!string.IsNullOrEmpty(domProbe))
                    {
                        _domSourceActive = true;
                        _recognizedText = NormalizeRecognizedText(domProbe);
                        NotifyBalloon("DOM-Text", $"{_recognizedText.Length} Zeichen aus Browser gelesen (exakt).", ToolTipIcon.Info);
                    }
                    else
                    {
                        NotifyBalloon(
                            "DOM nicht erreichbar",
                            "Edge mit starten-edge-typewriter.bat öffnen (edu.typewriter.at). Fallback: OCR.",
                            ToolTipIcon.Warning);
                    }
                }

                if (!_domSourceActive && _selectedRectangle is null)
                {
                    NotifyBalloon("Kein Bereich", "Für OCR zuerst einen Bildschirmbereich auswählen (oder DOM per BAT nutzen).", ToolTipIcon.Warning);
                    return;
                }

                var liveRectangle = _selectedRectangle ?? Rectangle.Empty;
                _tray?.SetStatus(_domSourceActive ? $"DOM-Start: {title}" : $"Live-OCR: {title}");
                report = await _typing.StartContinuousAsync(
                    _recognizedText,
                    _settings,
                    targetWindow,
                    title,
                    token => ReadLiveTextAsync(liveRectangle, token));
            }
            else
            {
                // Static mode remains available for fixed text fields.
                if (_settings.RefreshOcrOnStart)
                {
                    if (_selectedRectangle is not Rectangle rectangle)
                    {
                        NotifyBalloon("Kein Bereich", "Zuerst einen Bildschirmbereich auswählen.", ToolTipIcon.Warning);
                        return;
                    }

                    _tray?.SetStatus("OCR aktualisieren …");
                    var freshText = await CaptureOcrConsensusAsync(rectangle, CancellationToken.None);

                    if (string.IsNullOrEmpty(freshText))
                    {
                        NotifyBalloon("OCR leer", "Im gespeicherten Bereich wurde kein Text erkannt.", ToolTipIcon.Warning);
                        return;
                    }

                    _recognizedText = freshText;
                }

                if (string.IsNullOrEmpty(_recognizedText))
                {
                    NotifyBalloon("Kein Text", "Zuerst einen Bereich auswählen und OCR übernehmen.", ToolTipIcon.Warning);
                    return;
                }

                _tray?.SetStatus($"Start: {title}");
                report = await _typing.StartAsync(_recognizedText, _settings, targetWindow, title);
            }

            report.LiveOcrConsensusDisagreements = _consensusDisagreements;
            report.LiveOcrConsensusDroppedCharacters = _consensusDroppedCharacters;
            _logger.Save(report);

            _tray?.SetStatus(report.StopReason == "Completed" ? "Fertig" : report.StopReason);
            NotifyFinishBalloon(
                "Testlauf beendet",
                $"{report.StopReason}\n{report.TypedCharacters} Zeichen · {report.SentKeyEvents} Tasten · Live-OCR: {report.LiveOcrRefreshes}");
        }
        catch (Exception ex)
        {
            _tray?.SetStatus("Fehler");
            NotifyBalloon("Eingabefehler", ex.Message, ToolTipIcon.Error);
        }
    }

    private Task<string> ReadLiveTextAsync(Rectangle rectangle, CancellationToken token)
    {
        return Task.Run(async () =>
        {
            if (_settings.UseDomTextSource)
            {
                var dom = await _domReader.TryReadTypewriterRemainingAsync(token).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(dom))
                {
                    _domSourceActive = true;
                    return LiveTextMarkers.MarkDomRemaining(NormalizeRecognizedText(dom));
                }
            }

            if (rectangle.Width < 2 || rectangle.Height < 2)
                return string.Empty;

            // OCR fallback: consensus of two captures.
            return await CaptureOcrConsensusAsync(rectangle, token).ConfigureAwait(false);
        }, token);
    }

    private async Task<string> CaptureOcrConsensusAsync(Rectangle rectangle, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var first = await CaptureOcrOnceAsync(rectangle, token);
        token.ThrowIfCancellationRequested();
        await Task.Delay(45, token);
        token.ThrowIfCancellationRequested();
        var second = await CaptureOcrOnceAsync(rectangle, token);
        token.ThrowIfCancellationRequested();

        var agreed = OcrConsensus.Agree(first, second);
        if (agreed.HadDisagreement)
        {
            Interlocked.Increment(ref _consensusDisagreements);
            Interlocked.Add(ref _consensusDroppedCharacters, agreed.DroppedCharacters);
        }

        return agreed.Text;
    }

    private async Task<string> CaptureOcrOnceAsync(Rectangle rectangle, CancellationToken token)
    {
        using var screenshot = _capture.Capture(rectangle);
        using var prepared = ImagePreprocessor.Prepare(screenshot, _settings.OcrMode);
        var text = await _ocr.RecognizeAsync(prepared, _settings.OcrLanguageTag);
        token.ThrowIfCancellationRequested();
        return NormalizeRecognizedText(text);
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
        if (_keyboardLock?.IsLocked == true)
            _keyboardLock.SetLocked(false);
        _tray?.SetStatus("Gestoppt");
    }

    private void OnProgress(int current, int total, int sent)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            _tray?.SetStatus($"Tippt: {current} / {total} erkannt – {sent} Tasten"));
    }

    private void OnStatus(string message)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            _tray?.SetStatus(message));
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
            NotifyBalloon("Hotkey-Fehler", ex.Message, ToolTipIcon.Warning);
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
        _keyboardLock?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
    }
}
