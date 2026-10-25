# Rift Companion

**Nouvelle architecture : Electron + TypeScript**, avec API et worker Node.js et PostgreSQL local ou distant. Le parcours de migration ne dépend pas du SDK .NET. Lancer `Lancer-Electron.cmd` ; voir [installation, transition du serveur et contrôles](docs/ELECTRON.md). L’interface WPF est portée dans Electron (profil, graphiques, détails et grille d’overlay). Le plein écran exclusif reste en qualification.

Les versions ci-dessous décrivent l’application WPF historique, conservée pour comparaison et retour arrière.

**v41 : dix matchs par défaut**, collecte récente et pages de consultation limitées à dix ; statistiques de saison sur les données déjà stockées.

**v40 : chargement progressif des profils**, identité publiée avant les rangs et matchs, états de chargement par bloc et refus de clé Riot explicite. Voir `docs/REPRISE.md`.

**v39 : modes communs, top 3 récent et liste de saison serveur**, Flex visible, frise colorée par palier et popup de runes limitée à sa fenêtre active. La liste de saison lit les agrégats existants, sans lancer de rattrapage Riot ; couverture partielle indiquée.

**v38 : disposition compacte mesurée sur DeepLoL**, trois cartes alignées au-dessus de l’historique, largeur centrale fixe maximale, en-tête et classement allégés. Cette passe ne modifie pas les données.

**v37 : graphiques de profil** — anneau de winrate, barres verticales victoires/défaites par rôle, route des divisions Solo/Flex et moyennes K/D/A colorées. [Plan pour le graphique LP et les prochaines fonctions](docs/PROFILE-ROADMAP.md).

**v36 : profil compact inspiré de DeepLoL** : classement à gauche, bilan horizontal, champions et rôles côte à côte, historique dans la colonne principale. Filtres et détails existants conservés ; aucun appel réseau ajouté pour la présentation.

**v35 : lancement serveur par défaut**, port lu depuis la configuration locale, section de clé Riot retirée. La clé Riot se configure sur le backend uniquement. Voir [les consignes actuelles](docs/REPRISE.md). Les indications historiques de mode local ci-dessous ne décrivent plus le lanceur par défaut.

Application Windows en **C# / .NET 10 LTS, WPF et SQLite**. Le profil individuel et sa fluidité sont prioritaires ; la draft reste secondaire.

**v34 : mode client/serveur privé, collecte limitée aux 20 dernières parties.** API ASP.NET Core, worker Quartz, PostgreSQL et Docker Compose, matchs mutualisés et statistiques sur les parties affichées. Aucun import automatique de saison. [Reprise complète sur un autre PC, backend réel sur 5081](docs/REPRISE.md). Les deux lanceurs lisent le port configuré dans `server/.env` (5081 sur cette installation).

## Reprendre sur un autre PC

1. Installer Git et le **SDK .NET 10 pour Windows x64**, version `10.0.401` ou correctif compatible avec `native/global.json` : https://dotnet.microsoft.com/download/dotnet/10.0.
2. Cloner le dépôt puis compiler dans PowerShell :

```powershell
git clone https://github.com/Kalamare/RiftCompanion.git
cd RiftCompanion
powershell -NoProfile -ExecutionPolicy Bypass -File native/Build.ps1 -Check
.\Lancer-Natif.cmd
```

La compilation restaure les dépendances NuGet et produit `artifacts/native-profile-v41`. Les SDK locaux, les exécutables et les caches ne sont pas dans Git. Un accès Internet est nécessaire pour la première restauration.

3. Configurer et démarrer le backend Docker selon `docs/REPRISE.md`, avec la clé Riot côté serveur et la clé d’accès privée du client. Ouvrir le client LoL pour détecter son compte, ou rechercher un profil.
4. Si le dépôt existe déjà sur le second PC, depuis sa racine :

```powershell
git pull --ff-only
powershell -NoProfile -ExecutionPolicy Bypass -File native/Build.ps1 -Check
.\Lancer-Natif.cmd
```

État de reprise : **native-profile-v35** ; vérifications métier/WPF et tests PostgreSQL/API/SignalR. Lire [la fiche de reprise](docs/REPRISE.md) avant de continuer.

## Fonctionnalités du profil

- Compte Riot connecté détecté et dernier profil personnel conservé hors ligne. Recherche d'autres joueurs indépendante.
- Portrait du joueur, rangs Solo/Duo et Flex, statistiques, champions et historique.
- Barres de répartition des rôles et de victoires, calculées sur le filtre courant. ARAM, Arena et ARURF ne produisent pas de rôles classiques.
- Historique initial de 20 parties, puis « Voir plus » par pages de 10, y compris au-delà de 100 ; défilement conservé et statistiques affichées une fois prêtes.
- Clic sur une partie : équipes et lignes compactes, joueurs cliquables, sorts, runes, inventaire sur deux rangées, balise et quête de rôle, statistiques, objectifs et bans. Rangs actuels datés, chargés séparément et mis en cache. Portraits, sorts et runes livrés localement. Messages d’illustrations réservés au Débug ; chargement annulable et exemple fictif disponible.
- [Overlay en partie](docs/OVERLAY.md) : Ctrl+X pour afficher/masquer, Ctrl+Maj+X pour détacher sur un autre écran. Données historiques sur un échantillon borné ; exemple sans réseau disponible.
- Fenêtre « Diagnostic en direct » : CPU/RAM, mémoire .NET, threads, requêtes, chargements, cache des images et analyse automatique des signaux suspects. [Guide des métriques](docs/DIAGNOSTIC.md).
- Catalogue français des modes actualisable. Diagnostics masqués derrière le bouton Débug ; erreurs utiles toujours visibles.
- Rang mondial/serveur : [source automatique encore à intégrer](docs/CLASSEMENT-MONDIAL.md). Ne pas confondre le palier Riot avec une position mondiale.

## Validation et données locales

`native/Build.ps1 -Check` exécute les contrôles métier et WPF. Les contrôles avec réponses simulées et les rendus synthétiques ne constituent pas une validation réelle de Riot/LCU ni une mesure de l'impact sur les FPS.

Les données personnelles restent dans `%LOCALAPPDATA%\RiftCompanion` : clé chiffrée, profils, préférences SQLite et caches. Elles ne sont pas transférées par Git. Ne jamais committer de clé API, de lockfile LoL ou de capture contenant des identifiants sensibles.

## Documentation

- [Reprise sur un autre PC](docs/REPRISE.md)
- [Guide natif](native/README.md)
- [Profil personnel et reprise](docs/PROFIL-PERSONNEL.md)
- [Fluidité du profil](docs/FLUIDITE-PROFIL.md)
- [Décision de stack Windows](docs/ADR-001-STACK-WINDOWS.md)

Les ressources Riot restent la propriété de leurs ayants droit ; leurs sources sont indiquées dans les dossiers d'assets. Ce projet n'est pas affilié à Riot Games.

Les filtres de statistiques par mode, la synchronisation saisonnière et les sources étudiées sont décrits dans [Sources et calculs](docs/SOURCES-STATISTIQUES.md).
