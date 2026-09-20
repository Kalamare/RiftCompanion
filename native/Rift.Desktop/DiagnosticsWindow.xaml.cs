using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Rift.Core;

namespace Rift.Desktop;

public partial class DiagnosticsWindow : Window
{
    private readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private long lastTick = Stopwatch.GetTimestamp(), lastAllocation = GC.GetTotalAllocatedBytes();
    private TimeSpan lastCpu;
    private long lastRequests;
    private bool sampling, closed;
    private readonly DiagnosticAnalyzer analyzer = new();
    internal bool SamplingEnabled => timer.IsEnabled;
    public static void Open(Window? owner)
    {
        var existing = Application.Current.Windows.OfType<DiagnosticsWindow>().FirstOrDefault();
        if (existing is not null && existing.Owner == owner) { existing.WindowState = WindowState.Normal; existing.Activate(); return; }
        existing?.Close();
        new DiagnosticsWindow { Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner }.Show();
    }
    public DiagnosticsWindow()
    {
        InitializeComponent();
        using var process = Process.GetCurrentProcess(); lastCpu = process.TotalProcessorTime;
        Identity.Text = $"RiftCompanion.exe · PID {Environment.ProcessId} · .NET {Environment.Version} · {Environment.ProcessorCount} processeurs logiques";
        lastRequests = RuntimeDiagnostics.Snapshot().Summaries.Where(x => IsHttp(x.Category)).Sum(x => x.Completed);
        timer.Tick += async (_, _) => await Sample();
        Loaded += async (_, _) => { timer.Start(); await Sample(); };
        Closed += (_, _) => { closed = true; timer.Stop(); };
    }
    private static bool IsHttp(string category) => category is "Riot" or "LCU" or "CDN" or "Catalogue";
    internal async Task Sample()
    {
        if (sampling || closed || WindowState == WindowState.Minimized) return;
        sampling = true;
        try
        {
            var now = Stopwatch.GetTimestamp(); var elapsed = Math.Max(.001, Stopwatch.GetElapsedTime(lastTick, now).TotalSeconds); lastTick = now;
            var sample = await Task.Run(() => {
                using var p = Process.GetCurrentProcess(); p.Refresh();
                var threads = new List<ThreadSample>();
                foreach (ProcessThread t in p.Threads)
                    using (t) try { threads.Add(new(t.Id, t.ThreadState.ToString(), t.TotalProcessorTime.TotalMilliseconds)); }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                return (Cpu: p.TotalProcessorTime, Ram: p.WorkingSet64, Private: p.PrivateMemorySize64, Peak: p.PeakWorkingSet64, Handles: p.HandleCount, Threads: threads.ToArray());
            });
            var cpu = Math.Max(0, (sample.Cpu - lastCpu).TotalSeconds / elapsed / Environment.ProcessorCount * 100); lastCpu = sample.Cpu;
            var allocated = GC.GetTotalAllocatedBytes(); var rate = Math.Max(0, allocated - lastAllocation) / elapsed / 1048576; lastAllocation = allocated;
            var snapshot = RuntimeDiagnostics.Snapshot();
            var requests = snapshot.Summaries.Where(x => IsHttp(x.Category)).Sum(x => x.Completed);
            var requestRate = Math.Max(0, requests - lastRequests) / elapsed; lastRequests = requests;
            if (closed || PauseButton.IsChecked == true) return;
            var findings = analyzer.Observe(new(DateTimeOffset.UtcNow, cpu, sample.Private / 1048576.0, rate, Math.Max(0, elapsed - 1) * 1000), snapshot);
            AnalysisList.ItemsSource = findings;
            AnalysisStatus.Text = findings.Count > 0 ? $"{findings.Count} signal(s) à examiner · voir Analyse automatique" : analyzer.ObservedSeconds < 10
                ? "Analyse en cours : collecte des premiers relevés…" : "Aucun signal détecté selon les seuils actuels · mémoire : observation sur une minute";
            AnalysisStatus.Foreground = findings.Count > 0 ? System.Windows.Media.Brushes.SandyBrown : System.Windows.Media.Brushes.LightSeaGreen;
            var gc = GC.GetGCMemoryInfo();
            Metrics.Text = $"CPU {cpu:F2} %   ·   RAM résidente {sample.Ram / 1048576.0:F1} Mo   ·   Mémoire privée {sample.Private / 1048576.0:F1} Mo   ·   Pic RAM {sample.Peak / 1048576.0:F1} Mo";
            Memory.Text = $"Mémoire .NET {GC.GetTotalMemory(false) / 1048576.0:F1} Mo · Allocations {rate:F2} Mo/s · Tas au dernier GC {gc.HeapSizeBytes / 1048576.0:F1} Mo · Fragmentation {gc.FragmentedBytes / 1048576.0:F1} Mo · GC 0/1/2 : {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}";
            Runtime.Text = $"Threads {sample.Threads.Length} · Handles {sample.Handles} · ThreadPool {ThreadPool.ThreadCount}, travaux en attente {ThreadPool.PendingWorkItemCount} · Retard du relevé UI {Math.Max(0, elapsed - 1) * 1000:F0} ms · {DateTime.Now:HH:mm:ss}";
            Counters.Text = $"HTTP : {requests} terminées, {requestRate:F1}/s · {snapshot.Active.Length} opérations en cours   ·   " + string.Join("   ·   ", snapshot.Counters.Select(c => $"{c.Key} : {c.Value}"));
            ActiveList.ItemsSource = snapshot.Active; RecentList.ItemsSource = snapshot.Recent; SummaryList.ItemsSource = snapshot.Summaries; ThreadList.ItemsSource = sample.Threads;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        { if (!closed) Runtime.Text = "Relevé processus temporairement indisponible."; }
        finally { sampling = false; }
    }
    private sealed record ThreadSample(int Id, string State, double CpuMs);
}
