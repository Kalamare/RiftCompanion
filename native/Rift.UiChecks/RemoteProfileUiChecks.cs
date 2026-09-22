using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Rift.Contracts;
using Rift.Core;
using Rift.Desktop;

static class RemoteProfileUiChecks
{
    public static async Task Run(string folder)
    {
        var oldUrl = Environment.GetEnvironmentVariable("RIFT_SERVER_URL");
        var oldKey = Environment.GetEnvironmentVariable("RIFT_SERVER_ACCESS_KEY_FILE");
        var keyFile = Path.Combine(folder, "private-server-test-key"); File.WriteAllText(keyFile, new string('x', 32));
        Environment.SetEnvironmentVariable("RIFT_SERVER_URL", "http://127.0.0.1:1/");
        Environment.SetEnvironmentVariable("RIFT_SERVER_ACCESS_KEY_FILE", keyFile);
        var view = new ProfileView();
        try
        {
            view.Configure(Path.Combine(folder, "remote-ui"));
            T Control<T>(string name) => (T)view.FindName(name);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var first = ProfileDemo.Create().Matches[0] with { Id = "sample-1", Queue = 420, ChampionId = 103, Role = "middle", Kills = 6, Deaths = 2, Assists = 4, Remake = false, Seconds = 600 };
            var second = first with { Id = "sample-2", Queue = 450, ChampionId = 1, Kills = 0, Deaths = 4, Assists = 2 };
            var profile = new PlayerProfile("Serveur#TEST", "euw1", 100, [], [first, second], DateTimeOffset.UtcNow, false, 20) { Puuid = "test-player" };
            typeof(ProfileView).GetField("profile", flags)!.SetValue(view, profile);
            void Render() => typeof(ProfileView).GetMethod("Render", flags)!.Invoke(view, null);
            string Kda() => ((TextBlock)((StackPanel)Control<System.Windows.Controls.Primitives.UniformGrid>("MetricsPanel").Children[1]).Children[1]).Text;
            Render();
            if (Control<Expander>("StatisticsSettings").Visibility != Visibility.Collapsed) throw new Exception("Season settings visible in sample mode");
            if (!Control<TextBlock>("SampleText").Text.StartsWith("2 parties") || Kda() != 2.0.ToString("F2") || Control<ListBox>("ChampionsGrid").Items.Count != 2)
                throw new Exception("Displayed sample totals or weighted KDA incorrect");
            Control<ComboBox>("SummaryMode").SelectedValue = 420;
            if (!Control<TextBlock>("SampleText").Text.StartsWith("1 parties") || Kda() != 5.0.ToString("F2") || Control<ListBox>("ChampionsGrid").Items.Count != 2)
                throw new Exception("Independent sample filter incorrect");
            Control<ComboBox>("SummaryMode").SelectedValue = 0;
            profile = profile with { Matches = [first, second, first with { Id = "sample-3", Kills = 12, Deaths = 0, Assists = 0 }] };
            typeof(ProfileView).GetField("profile", flags)!.SetValue(view, profile); Render();
            if (!Control<TextBlock>("SampleText").Text.StartsWith("3 parties") || Kda() != 4.0.ToString("F2")) throw new Exception("Pagination did not expand sample statistics");
            if (!Control<TextBlock>("ChampionsScope").Text.Contains("pas un bilan de saison")) throw new Exception("Sample scope not explicit");
            typeof(ProfileView).GetField("personalProfile", flags)!.SetValue(view, profile with { RiotId = "Moi#TEST", Puuid = "personal" });
            typeof(ProfileView).GetField("browsingOther", flags)!.SetValue(view, true);
            Control<TextBox>("RiotIdBox").Text = "TexteNonValide";
            typeof(ProfileView).GetMethod("OnPersonalProfile", flags)!.Invoke(view, [Control<Button>("RefreshPersonalButton"), new RoutedEventArgs()]);
            await (Task)typeof(ProfileView).GetField("loading", flags)!.GetValue(view)!;
            if (Control<TextBox>("RiotIdBox").Text != profile.RiotId || !(bool)typeof(ProfileView).GetField("browsingOther", flags)!.GetValue(view)! ||
                ((PlayerProfile?)typeof(ProfileView).GetField("profile", flags)!.GetValue(view))?.Puuid != profile.Puuid)
                throw new Exception("Refresh navigated to personal account or lost current profile on failure");
            var serverNow = DateTimeOffset.UtcNow.AddHours(2);
            typeof(ProfileView).GetField("refreshStatus", flags)!.SetValue(view, new RefreshStatus("cooldown", serverNow, serverNow.AddMinutes(-1), serverNow.AddMinutes(5)));
            typeof(ProfileView).GetField("refreshReceivedAt", flags)!.SetValue(view, DateTimeOffset.UtcNow);
            typeof(ProfileView).GetMethod("RenderRefreshStatus", flags)!.Invoke(view, null);
            if (Control<Button>("RefreshPersonalButton").IsEnabled || !Control<Button>("RefreshPersonalButton").Content.ToString()!.Contains("04:"))
                throw new Exception("Cooldown did not use server time or failed to disable refresh");
            typeof(ProfileView).GetField("refreshStatus", flags)!.SetValue(view, new RefreshStatus("ready", serverNow, null, serverNow.AddSeconds(-1)));
            typeof(ProfileView).GetMethod("RenderRefreshStatus", flags)!.Invoke(view, null);
            if (!Control<Button>("RefreshPersonalButton").IsEnabled) throw new Exception("Expired cooldown still disables refresh");
            Console.WriteLine("OK WPF : mode serveur sans clé Riot ni synchronisation, agrégats indépendants de l’historique récent, filtres distincts et couverture partielle explicite.");
        }
        finally
        {
            await view.StopAsync();
            Environment.SetEnvironmentVariable("RIFT_SERVER_URL", oldUrl);
            Environment.SetEnvironmentVariable("RIFT_SERVER_ACCESS_KEY_FILE", oldKey);
        }
    }
}
