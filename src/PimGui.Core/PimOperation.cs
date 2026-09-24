using System.Text;

namespace PimGui.Core;

public enum OperationPhase { Preparing, Downloading, Verifying, Extracting, Finalizing, Stopping }
public sealed record OperationProgress(OperationPhase Phase, int? Percent = null, bool Approximate = false,
    long? DownloadedBytes = null, long? TotalBytes = null, double? BytesPerSecond = null, TimeSpan? Remaining = null);

/// <summary>One user operation: cancellation and stage progress, independent of any page.</summary>
public sealed class PimOperation(Action<OperationProgress>? changed = null) : IDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly object gate = new();
    private readonly StringBuilder progressLine = new();
    private OperationProgress current = new(OperationPhase.Preparing);
    public CancellationToken Token => cancellation.Token;
    public bool IsCancellationRequested => cancellation.IsCancellationRequested;
    public OperationProgress Current { get { lock (gate) return current; } }
    public void Cancel()
    {
        cancellation.Cancel();
        Report(OperationPhase.Stopping);
    }
    public void Report(OperationPhase phase, int? percent = null, bool approximate = false)
    {
        lock (gate)
        {
            if (IsCancellationRequested && phase != OperationPhase.Stopping) return;
            var next = new OperationProgress(phase, percent is null ? null : Math.Clamp(percent.Value, 0, 100), approximate);
            if (next == current) return;
            current = next;
            try { changed?.Invoke(next); } catch (Exception) { }
        }
    }
    public void Observe(ProcessOutput fragment)
    {
        if (fragment.IsError || IsCancellationRequested) return;
        lock (gate)
        {
            foreach (var character in fragment.Text)
            {
                if (character is '\r' or '\n') { ParseLine(ended: true); progressLine.Clear(); }
                else if (progressLine.Length < 256) progressLine.Append(character);
            }
            ParseLine();
        }
    }
    public void Transfer(long bytes, long? total, double? speed, TimeSpan? remaining)
    {
        lock (gate)
        {
            if (IsCancellationRequested) return;
            current = new(OperationPhase.Downloading, total is > 0 ? (int)Math.Min(100, bytes * 100 / total.Value) : null,
                false, bytes, total, speed, remaining);
            try { changed?.Invoke(current); } catch (Exception) { }
        }
    }
    private void ParseLine(bool ended = false)
    {
        var line = progressLine.ToString();
        foreach (var (label, phase, next) in new[]
        {
            ("Downloading", OperationPhase.Downloading, OperationPhase.Verifying),
            ("Extracting", OperationPhase.Extracting, OperationPhase.Finalizing)
        })
        {
            if (!line.StartsWith(label + ": ", StringComparison.Ordinal)) continue;
            var tail = line[(label.Length + 2)..];
            if (tail.Contains('✅')) { Report(next); return; }
            if (tail.Contains('❌') || tail.Contains('⏸') || tail.Contains('|')) { Report(OperationPhase.Preparing); return; }
            if (tail.Any(c => c != '.')) return;
            // PIM 26.3 ProgressPrinter: fixed 80 columns, dots = floor(percent * width / 100).
            // This is a rounded stage estimate, never a made-up overall installation percentage.
            var width = 80 - 3 - label.Length;
            // PIM 26.3 with redirected/no-colour output completes the full dotted line without a checkmark.
            if (ended && tail.Length >= width) { Report(next); return; }
            Report(phase, Math.Min(99, tail.Length * 100 / width), approximate: true);
            return;
        }
    }
    public void Dispose() => cancellation.Dispose();
}
