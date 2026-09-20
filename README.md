# Rift Companion

Application Windows en **C# / .NET 10 LTS, WPF et SQLite**. Le profil individuel et sa fluidité sont prioritaires ; la draft reste secondaire.

## Reprendre sur un autre PC

1. Installer Git et le **SDK .NET 10 pour Windows x64**, version `10.0.401` ou correctif compatible avec `native/global.json` : https://dotnet.microsoft.com/download/dotnet/10.0.
2. Cloner le dépôt puis compiler dans PowerShell :

```powershell
git clone https://github.com/Kalamare/RiftCompanion.git
cd RiftCompanion
powershell -NoProfile -ExecutionPolicy Bypass -File native/Build.ps1 -Check
.\Lancer-Natif.cmd
```

La compilation restaure les dépendances NuGet et produit `artifacts/native-profile-v20`. Les SDK locaux, les exécutables et les caches ne sont pas dans Git. Un accès Internet est nécessaire pour la première restauration.

3. Dans l'application, renseigner sa clé Riot dans « Connexion aux données Riot ». Elle est chiffrée pour le compte Windows courant : il faut la saisir à nouveau sur le second PC. Ouvrir le client LoL pour détecter son compte, ou rechercher un profil. Configurer le dossier de LoL dans « Connexion et stockage » si nécessaire.
4. Si le dépôt existe déjà sur le second PC, depuis sa racine :

```powershell
git pull --ff-only
powershell -NoProfile -ExecutionPolicy Bypass -File native/Build.ps1 -Check
.\Lancer-Natif.cmd
```

État de reprise : **native-profile-v20** ; 136 contrôles métier et contrôles WPF réussis. Lire [la fiche de reprise](docs/REPRISE.md) avant de continuer.

## Fonctionnalités du profil

- Compte Riot connecté détecté et dernier profil personnel conservé hors ligne. Recherche d'autres joueurs indépendante.
- Portrait du joueur, rangs Solo/Duo et Flex, statistiques, champions et historique.
- Barres de répartition des rôles et de victoires, calculées sur le filtre courant. ARAM, Arena et ARURF ne produisent pas de rôles classiques.
- Historique initial de 20 parties, puis « Voir plus » par pages de 10, y compris au-delà de 100 ; défilement conservé et statistiques affichées une fois prêtes.
- Clic sur une partie : équipes et lignes compactes, joueurs cliquables, sorts, runes, inventaire sur deux rangées, balise et quête de rôle, statistiques, objectifs et bans. Rangs actuels datés, chargés séparément et mis en cache. Portraits, sorts et runes livrés localement. Messages d’illustrations réservés au Débug ; chargement annulable et exemple fictif disponible.
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
