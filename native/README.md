# Client WPF historique

Le client actif est [Electron](../docs/ELECTRON.md). Ce dossier conserve l’ancienne application C#/.NET 10 WPF pour comparaison visuelle et retour arrière. Ses assets restent utilisés par le build Electron ; les notices de provenance ne sont pas obsolètes.

## Compiler et vérifier l’ancien client

Installer sous Windows le SDK compatible avec [global.json](global.json), puis depuis la racine du dépôt :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File native/Build.ps1 -Check
.\Lancer-Natif.cmd
```

Le lanceur choisit la version produite par le script de build. Ne pas reprendre un ancien numéro `native-profile-v*` depuis une note de session.

Pour utiliser le mode serveur WPF, revenir au backend .NET selon [server/README.md](../server/README.md), puis utiliser `Lancer-Serveur-Local.cmd`. Le backend TypeScript ne fournit pas SignalR. Le mode local historique et ses secrets restent sous `%LOCALAPPDATA%\RiftCompanion`.

La référence graphique se trouve dans `Rift.Desktop/ProfileView.xaml`, `MatchDetailsView.xaml`, `OverlayWindow.xaml` et `MainWindow.xaml`. Les contrôles WPF ne remplacent pas les tests Electron ni une mesure en jeu.

Les anciennes procédures détaillées restent accessibles dans l’[historique documentaire](../docs/ARCHIVES.md).
