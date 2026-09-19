# ADR-001 — C# / .NET 10 LTS, WPF et SQLite

Statut : accepté par l'utilisateur le 16 septembre 2026.

## Décision

L'application desktop est développée en C# sur .NET 10 LTS avec une interface WPF et un stockage local SQLite via Microsoft.Data.Sqlite. Cible actuelle : Windows x64. Le prototype Node reste disponible comme référence pendant la migration.

L'utilisateur a choisi cette stack. Les essais comparatifs Tauri/WPF proposés dans le plan initial ne sont donc plus un préalable au développement. Cette décision ne constitue pas une preuve de supériorité en performance ; RC-03 et la campagne de performance restent nécessaires.

## Structure initiale

- `native/Rift.Core` : modèles de draft et normalisation, sans réseau ni UI.
- `native/Rift.Infrastructure` : lecture du lockfile, transport HTTPS local, monitor séquentiel, stockage SQLite.
- `native/Rift.Desktop` : fenêtre WPF et orchestration ; traitements réseau et SQLite hors du thread UI.
- `native/Rift.Checks` : contrôles exécutables du domaine, du monitor avec transport simulé et d'une vraie base SQLite temporaire.

La fenêtre initiale conserve un code-behind limité. Passer à des ViewModels quand les écrans de recommandations et les commandes deviennent plus complexes ; éviter de développer un framework interne dès le départ.

## Version et dépendances

- SDK local : 10.0.401, dans `.tools/dotnet` ; runtime desktop 10.0.12.
- Paquet Microsoft.Data.Sqlite : 10.0.12 ; fichiers de verrouillage NuGet conservés.
- Aucune dépendance Node, navigateur ou WebView dans l'exécutable WPF.
- Installation SDK locale au projet, sans remplacement des SDK système.

## Limites du premier socle

- Sondage borné identique au prototype ; événements LCU à étudier séparément.
- Préférences SQLite réellement persistées ; cache de statistiques et schémas associés non implémentés.
- Mode démo sans requêtes LCU pendant son activation ; compteurs de session conservés quand on change de mode.
- Recommandations, équipement, statistiques joueurs, overlay et installateur restent à développer.
- Mesures intégrées CPU/RAM du processus WPF ; pas de mesure automatique des FPS ni de preuve d'absence de lag.

## Stockage et protection des données

Base par défaut : `%LOCALAPPDATA%\RiftCompanion\settings.db`. Le paramètre `--data-dir` permet un dossier isolé pour les tests. Rôle préféré et chemin d'installation uniquement ; aucun secret LCU sauvegardé. Schéma versionné, refus d'une base plus récente, requêtes SQL paramétrées.

Le transport HTTPS est limité à trois endpoints en lecture seule sur 127.0.0.1. Les redirections et proxies sont désactivés ; exception au certificat auto-signé limitée à l'origine loopback LCU. L'arrêt annule les requêtes et attend les écritures déjà lancées.
