using System.IO;
using System.Text;
using AutoInputTester.Models;

namespace AutoInputTester.Logging;

public sealed class TestLogger
{
    public string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoInputTester", "Logs");

    public string Save(TestRunReport report)
    {
        Directory.CreateDirectory(LogDirectory);
        var stamp = report.StartedAt.ToString("yyyyMMdd_HHmmss");
        var path = Path.Combine(LogDirectory, $"run_{stamp}_{Guid.NewGuid():N}.txt");
        var sb = new StringBuilder();
        sb.AppendLine("AUTO INPUT TESTER – TESTLAUF");
        sb.AppendLine($"Start: {report.StartedAt:O}");
        sb.AppendLine($"Ende: {report.FinishedAt:O}");
        sb.AppendLine($"Zielfenster: {report.TargetWindowTitle}");
        sb.AppendLine($"Erkannte Sollzeichen gesamt: {report.TargetCharacters}");
        sb.AppendLine($"Davon abgearbeitet: {report.TypedCharacters}");
        sb.AppendLine($"Gesendete Tastendrücke: {report.SentKeyEvents}");
        sb.AppendLine($"Fehler erzeugt: {report.PlannedErrors}");
        sb.AppendLine($"Mit Backspace korrigierte Fehler: {report.CorrectedErrors}");
        sb.AppendLine($"Fokus-Wiederherstellungen: {report.FocusRestores}");
        sb.AppendLine($"Live-OCR Durchläufe: {report.LiveOcrRefreshes}");
        sb.AppendLine($"Durch Live-OCR nachgeladene Zeichen: {report.LiveOcrAppendedCharacters}");
        sb.AppendLine($"Live-OCR Ausrichtungsfehler: {report.LiveOcrAlignmentMisses}");
        sb.AppendLine($"OCR-Konsens Uneinigkeiten: {report.LiveOcrConsensusDisagreements}");
        sb.AppendLine($"OCR-Konsens verworfene unsichere Zeichen: {report.LiveOcrConsensusDroppedCharacters}");
        sb.AppendLine($"OCR-Neusynchronisationen: {report.LiveOcrResyncs}");
        sb.AppendLine($"Solltempo: {report.RequestedStrokesPer10Minutes} / 10 min");
        sb.AppendLine($"Isttempo: {report.ActualStrokesPer10Minutes:F1} / 10 min");
        sb.AppendLine($"Dauer: {report.DurationSeconds:F2} s");
        sb.AppendLine($"Seed: {report.Seed}");
        sb.AppendLine($"Endstatus: {report.StopReason}");
        sb.AppendLine("Fehlerpositionen: " + (report.ErrorPositions.Count == 0 ? "keine" : string.Join(", ", report.ErrorPositions)));
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        return path;
    }
}
