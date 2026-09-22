# Reprise — 22 septembre 2026

## Version active

**native-profile-v34**, branche `main`. Application WPF .NET 10 ; profil et fluidité prioritaires. Direction active : mode client/serveur privé (ASP.NET Core, Quartz, PostgreSQL, Docker Compose). Le mode serveur ne contacte pas OP.GG.

- Collecte limitée aux **20 derniers matchs** : `RIFT_HISTORY_BACKFILL=false`. Anciens matchs et curseurs conservés, aucun rattrapage de saison automatique.
- KDA, bilan, champions et rôles calculés sur les parties chargées et filtrées, hors remakes. « Voir plus » agrandit cet échantillon avec les pages déjà stockées, sans appel Riot supplémentaire.
- Les compteurs victoires/défaites du classement Riot restent distincts de cet échantillon. Pas de limite de rétention à 30 jours déduite de DeepLoL.
- Actualiser conserve le profil affiché. Cooldown serveur partagé de **5 minutes** ; budgets privés : **5 actualisations acceptées et 3 nouvelles recherches / 10 minutes**. Consultations connues accessibles.
- Jobs récents distincts des jobs de saison, notifications SignalR, matchs mutualisés entre participants sans suivi récursif.
- Schéma PostgreSQL **v2** : ne pas relancer un ancien backend v31/v32 sur cette base.
- Overlay Ctrl+X, détachement Ctrl+Maj+X, déplacement, transparence, bannière ; détails des parties et profils cliquables ; diagnostic en direct.

## Sur le second PC : backend réel

Installer Git, le SDK .NET 10 Windows x64 compatible avec `native/global.json` (10.0.401) et Docker Desktop avec conteneurs Linux. Démarrer Docker Desktop. Dans un clone propre :

```powershell
git pull --ff-only
powershell -NoProfile -ExecutionPolicy Bypass -File native/Build.ps1 -Check
powershell -NoProfile -ExecutionPolicy Bypass -File server/Initialize-Dev.ps1
notepad .\server\.secrets\riot-key
```

Placer uniquement la clé Riot valide dans ce fichier. Si nécessaire, cloner auparavant avec `git clone https://github.com/Kalamare/RiftCompanion.git`, puis `cd RiftCompanion`.

Dans **la même fenêtre PowerShell** :

```powershell
$env:RIFT_FIXTURE_MODE = 'false'
$env:RIFT_PORT = '5081'
$env:RIFT_HISTORY_BACKFILL = 'false'
docker compose -p rift-real -f server/compose.yaml up -d --build
Invoke-RestMethod http://127.0.0.1:5081/health

$env:RIFT_SERVER_URL = 'http://127.0.0.1:5081/'
$env:RIFT_SERVER_ACCESS_KEY_FILE = "$PWD\server\.secrets\access-key"
.\Lancer-Natif.cmd
```

La santé doit indiquer `data: riot`. Pour conserver la configuration Compose, inscrire `RIFT_PORT=5081`, `RIFT_FIXTURE_MODE=false` et `RIFT_HISTORY_BACKFILL=false` dans `server/.env`.

**Le lanceur `Lancer-Serveur-Local.cmd` cible 5080**, pas 5081. Utiliser les commandes ci-dessus pour `rift-real`. Les variables PowerShell doivent être définies de nouveau dans une nouvelle fenêtre. Sans `RIFT_SERVER_URL`, `Lancer-Natif.cmd` utilise le mode local historique.

## Données non transférées par Git

`server/.secrets`, `server/.env`, le volume PostgreSQL, les exécutables, `.tools`, caches et profils de `%LOCALAPPDATA%\RiftCompanion` restent locaux. La clé chiffrée Windows ne se transfère pas entre PC.

Le second PC aura une nouvelle base, sauf transfert explicite de sauvegarde. Une sauvegarde antérieure à v33 existe uniquement sur le premier PC dans `server/.secrets/before-v33-*.dump` ; elle ne représente pas forcément le dernier état. Pour reprendre les données actuelles, réaliser une nouvelle sauvegarde PostgreSQL et la transférer séparément par un canal privé. Ne pas utiliser `down -v` si les données doivent être conservées.

## Validation et suite

Validation v34 : **172 contrôles métier et contrôles WPF**, compilation sans avertissement (`native/Build.ps1 -Check`) ; **37 contrôles PostgreSQL**, REST/SignalR et cooldown (`server/Check.ps1`, projet fictif isolé). Ces tests ne mesurent pas les FPS en jeu. Les contrôles saisonniers restent présents pour le mode historique optionnel.

Avant distribution publique : authentification et budgets par compte, TLS, droits Riot Production, rétention, sauvegarde/restauration opérationnelle et tests de charge. Le limiteur Riot reste conservateur. Aucun rang mondial/serveur saisi manuellement ni estimé depuis les LP.

Lire `server/README.md`, `docs/ACTUALISATION-QUOTAS.md`, `docs/ARCHITECTURE-PUBLIQUE.md`, `docs/PROFIL-PERSONNEL.md`, `docs/OVERLAY.md` et `docs/DIAGNOSTIC.md`.

Prompt de reprise : « Reprenons Rift Companion v34. Lis docs/REPRISE.md et docs/ACTUALISATION-QUOTAS.md. Vérifie Git, .NET et Docker. Backend réel rift-real sur 5081 ; collecte limitée à 20 matchs, statistiques sur les parties affichées, pas de rattrapage saisonnier. Les données et secrets du premier PC ne sont pas dans Git. Le profil et sa fluidité restent prioritaires. »
