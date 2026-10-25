# Rift Companion

Application Windows pour consulter les profils League of Legends, l’historique, les statistiques et les joueurs en partie. Le client **Electron / TypeScript** utilise une API et un worker **Node.js 24**, avec **PostgreSQL 17** sur le PC ou un serveur distant privé.

La version en qualification est sur **`codex/electron-migration`**, dans la [PR #5](https://github.com/Kalamare/RiftCompanion/pull/5). Elle ne nécessite pas .NET. L’interface reprend le travail réalisé en WPF : profil, graphiques, détails des parties et grille d’overlay. Le plein écran exclusif n’est pas pris en charge par l’overlay Electron actuel.

## Démarrer ou mettre à jour

Sur Windows, installer Node.js 24 LTS pour lancer depuis les sources. Docker Desktop est nécessaire uniquement si le backend tourne sur ce PC.

```powershell
git fetch origin
git switch codex/electron-migration
git pull --ff-only
npm ci
.\Lancer-Electron.cmd
```

Pour une première installation, configurer d’abord le [backend privé](server/README.md). Dans les réglages de l’application, utiliser l’adresse réellement publiée par Docker et la **clé d’accès serveur**, distincte de la clé Riot. Le port par défaut et celui constaté sur le PC actuel sont **5080**.

Un exécutable portable est également produit par le workflow [Electron and TypeScript](https://github.com/Kalamare/RiftCompanion/actions/workflows/electron.yml) : télécharger `RiftCompanion-Electron-Windows` d’une exécution réussie de la branche, extraire entièrement le ZIP, puis lancer `RiftCompanion.exe`.

## Documentation

| Besoin | Référence |
|---|---|
| Installation, mise à jour, dépannage | [Guide Electron](docs/ELECTRON.md) |
| Backend, sauvegarde, transition depuis .NET | [Exploitation serveur](server/README.md) |
| Modules, stockage, contrats et choix techniques | [Architecture](docs/ARCHITECTURE.md) |
| Statistiques, pagination, sources et quotas | [Données](docs/DONNEES.md) |
| Overlay et état du plein écran | [Overlay](docs/OVERLAY.md) |
| Prochaines étapes | [Feuille de route](docs/ROADMAP.md) |
| Tests et recette Windows/LoL | [Validation](docs/validation.md) |
| Anciennes versions et documents remplacés | [Archives](docs/ARCHIVES.md) |

[Projet Notion](https://app.notion.com/p/3dd3a122b70381e48a1ffd665ce1ecde) : suivi des décisions et tâches. Les commandes techniques de référence sont maintenues dans ce dépôt.

## Développement

```powershell
npm ci
npm run check
npx playwright install chromium
npm run test:ui
```

Le code actif se trouve dans `apps/desktop`, `apps/server` et `packages/core`. `native/` et les projets C# de `server/` servent au retour arrière ; leurs assets et le schéma SQL restent utilisés par le build actuel. Ne pas les retirer sans migrer ces dépendances.

Rift Companion n’est pas approuvé par Riot Games. La distribution publique, la signature et les mesures de performances en jeu restent à qualifier.
