namespace AutoInputTester.Models;

public sealed class TestRunReport
{
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public int TargetCharacters { get; set; }
    public int TypedCharacters { get; set; }
    public int SentKeyEvents { get; set; }
    public int PlannedErrors { get; set; }
    public int CorrectedErrors { get; set; }
    public int FocusRestores { get; set; }
    public int LiveOcrRefreshes { get; set; }
    public int LiveOcrAppendedCharacters { get; set; }
    public int LiveOcrAlignmentMisses { get; set; }
    public int LiveOcrConsensusDisagreements { get; set; }
    public int LiveOcrConsensusDroppedCharacters { get; set; }
    public int LiveOcrResyncs { get; set; }
    public int RequestedStrokesPer10Minutes { get; set; }
    public int Seed { get; set; }
    public string TargetWindowTitle { get; set; } = string.Empty;
    public List<int> ErrorPositions { get; set; } = new();
    public string StopReason { get; set; } = "Completed";

    public double DurationSeconds => Math.Max(0, (FinishedAt - StartedAt).TotalSeconds);
    public double ActualStrokesPer10Minutes => DurationSeconds <= 0 ? 0 : SentKeyEvents / DurationSeconds * 600.0;
}
