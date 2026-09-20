using System.Diagnostics;

namespace Rift.Core;

public sealed record DiagnosticOperation(long Id, string Category, string Name, DateTimeOffset Started, double Milliseconds, string Result)
{
    public DateTimeOffset LocalStarted => Started.ToLocalTime();
}
public sealed record DiagnosticSummary(string Category, long Completed, long Errors, long Cancelled, int Active, double AverageMs);
public sealed record DiagnosticSnapshot(DiagnosticOperation[] Active, DiagnosticOperation[] Recent, DiagnosticSummary[] Summaries,
    IReadOnlyDictionary<string, long> Counters);

// Fixed labels only: never pass URLs, identities, headers, file paths or exception messages.
// Bounded in-memory journal; no subscriber callbacks on the measured threads.
public static class RuntimeDiagnostics
{
    private static readonly object gate = new();
    private static readonly Dictionary<long, Operation> active = [];
    private static readonly Queue<DiagnosticOperation> recent = new();
    private static readonly Dictionary<string, Totals> totals = [];
    private static readonly Dictionary<string, long> counters = [];
    private static long sequence;
    private sealed class Totals { public long Count, Errors, Cancelled; public double Milliseconds; }
    public static Operation Begin(string category, string name)
    {
        lock (gate)
        {
            var operation = new Operation(++sequence, category, name);
            active.Add(operation.Id, operation); return operation;
        }
    }
    public static void Count(string name) { lock (gate) counters[name] = counters.GetValueOrDefault(name) + 1; }
    public static DiagnosticSnapshot Snapshot()
    {
        lock (gate) return new(active.Values.Select(x => x.Row("En cours")).ToArray(), recent.Reverse().ToArray(),
            totals.Keys.Union(active.Values.Select(x => x.Category)).Select(key => {
                var t = totals.GetValueOrDefault(key) ?? new Totals();
                return new DiagnosticSummary(key, t.Count, t.Errors, t.Cancelled, active.Values.Count(x => x.Category == key), t.Count == 0 ? 0 : t.Milliseconds / t.Count);
            }).ToArray(), new Dictionary<string, long>(counters));
    }
    public sealed class Operation : IDisposable
    {
        internal long Id { get; }
        internal string Category { get; }
        private readonly string name;
        private readonly DateTimeOffset started = DateTimeOffset.UtcNow;
        private readonly long tick = Stopwatch.GetTimestamp();
        private bool disposed, error, cancelled;
        private string result = "Terminé";
        internal Operation(long id, string category, string name) { Id = id; Category = category; this.name = name; }
        public void HttpStatus(int status) { result = "HTTP " + status; error = status >= 400; }
        public void Failed() { result = "Erreur"; error = true; }
        public void Cancelled() { result = "Annulé / délai dépassé"; cancelled = true; }
        internal DiagnosticOperation Row(string state) => new(Id, Category, name, started, Stopwatch.GetElapsedTime(tick).TotalMilliseconds, state);
        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return; disposed = true; active.Remove(Id);
                var row = Row(result); recent.Enqueue(row); while (recent.Count > 200) recent.Dequeue();
                if (!totals.TryGetValue(Category, out var t)) totals[Category] = t = new();
                t.Count++; t.Milliseconds += row.Milliseconds; if (error) t.Errors++; if (cancelled) t.Cancelled++;
            }
        }
    }
}
