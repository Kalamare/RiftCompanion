using System.Windows.Documents;
using System.Windows.Media;
using Rift.Core;

namespace Rift.Desktop;

public partial class ProfileView
{
    private void RenderSummaryChart(ProfileSummary summary)
    {
        WinrateChart.Content = ProfileCharts.Donut(summary.Wins, summary.Games);
        AverageKda.Inlines.Clear();
        CompactMetrics.Text = summary.Games == 0 ? "Aucune partie" : $"{summary.Kda:F2} KDA · {summary.CsPerMinute:F1} CS/min";
        CompactMetrics.ToolTip = summary.Participation is { } kp ? $"Participation aux éliminations : {kp:F1} %" : "Participation aux éliminations indisponible";
        if (summary.Games == 0) return;
        AverageKda.Inlines.Add(new Run($"{summary.Kills:F1}") { Foreground = Brushes.Turquoise });
        AverageKda.Inlines.Add(new Run(" / "));
        AverageKda.Inlines.Add(new Run($"{summary.Deaths:F1}") { Foreground = ProfileCharts.Loss });
        AverageKda.Inlines.Add(new Run(" / "));
        AverageKda.Inlines.Add(new Run($"{summary.Assists:F1}") { Foreground = Brushes.SandyBrown });
        AverageKda.ToolTip = "Kills / morts / assists moyens par partie";
    }
}
