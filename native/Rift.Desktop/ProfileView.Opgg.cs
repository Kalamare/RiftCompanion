using System.Windows;
using Rift.Core;
using Rift.Infrastructure;

namespace Rift.Desktop;

public partial class ProfileView
{
    private OpggClient? opgg;
    private bool opggEnabled;
    private CancellationTokenSource? opggCancellation;
    private Task opggLoading = Task.CompletedTask;
    public void EnableOpgg() => opggEnabled = true;
    internal async Task<OpggProfile?> LoadOverlayOpgg(string id, string platform, CancellationToken token)
    {
        if (remote is not null || !opggEnabled || opgg is null) return null;
        while (isPreparing) await Task.Delay(500, token);
        return await Task.Run(() => opgg.LoadAsync(id, platform, token), token);
    }
    private void StartOpgg(PlayerProfile selected)
    {
        opggCancellation?.Cancel();
        var oldCancellation = opggCancellation;
        var oldLoading = opggLoading;
        opggCancellation = CancellationTokenSource.CreateLinkedTokenSource(personalLifetime.Token);
        OpggPanel.Visibility = Visibility.Collapsed;
        OpggRank.Text = "Rang serveur · chargement indépendant…";
        OpggDate.Text = "Le palier et les LP ci-dessus restent fournis par Riot. Rang mondial et rang Flex distinct non disponibles.";
        OpggChampions.ItemsSource = null; OpggScope.Text = "";
        opggLoading = Run();
        async Task Run()
        {
            var token = opggCancellation.Token;
            try
            {
                await oldLoading; oldCancellation?.Dispose();
                await RestoreStatistics(selected, token);
                if (remote is not null || !opggEnabled || selected.Demo || opgg is null) return;
                var result = await Task.Run(() => opgg.LoadAsync(selected.RiotId, selected.Platform, token), token);
                await PrepareSeasonImages(result, token);
                token.ThrowIfCancellationRequested();
                if (stopping || profile?.RiotId != selected.RiotId || profile.Platform != selected.Platform || profile.Demo) return;
                ShowOpgg(result);
            }
            catch (OperationCanceledException) { }
            catch (ArgumentException) { if (!token.IsCancellationRequested) ShowOpgg(null); }
        }
    }
    internal void ShowOpgg(OpggProfile? value)
    {
        seasonProfile = value;
        OpggRank.Text = value is null ? "Données indisponibles · profil Riot conservé" : value.RankLabel;
        OpggDate.Text = value is null ? "Aucune donnée complémentaire disponible pour le moment." : value.DateLabel +
            (DateTimeOffset.UtcNow - value.FetchedAt >= TimeSpan.FromHours(1) ? " · cache ancien, actualisation indisponible" : " · cache 1 h") + " · mondial / Flex distinct indisponibles";
        OpggScope.Text = value is null ? "" : value.Scope + " · liste potentiellement partielle · K/D/A moyens";
        OpggChampions.ItemsSource = value?.Champions.OrderByDescending(c => c.Games).ToArray();
        RenderRankCards(); RenderSeason();
    }
    internal async Task PrepareSeasonImages(OpggProfile? value, CancellationToken token)
    {
        if (value is null || assets is null) return;
        var prepared = await Task.Run(() => value.Champions.Select(c => (Champion: c,
            Name: assets.ChampionName(c.Id, c.Name), Icon: bitmaps.Get(assets.ChampionImage(c.Id, c.Name), 120))).ToArray(), token);
        token.ThrowIfCancellationRequested();
        foreach (var item in prepared) Visual(item.Champion.Id, item.Champion.Name, true).Update(item.Name, item.Icon);
    }
}
