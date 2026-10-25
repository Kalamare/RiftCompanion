# Installer et utiliser Electron

Référence actuelle : branche `codex/electron-migration`, mise à jour du 25 octobre 2026. Pour le backend, suivre [server/README.md](../server/README.md). Les anciens lanceurs `Lancer-Natif.cmd` et `Lancer-Backend-Local.cmd` concernent le parcours .NET.

## Installer depuis les sources

Prérequis : Windows, Git et Node.js 24 LTS. Pour héberger le backend sur le PC, installer aussi Docker Desktop avec les conteneurs Linux.

```powershell
git clone --branch codex/electron-migration https://github.com/Kalamare/RiftCompanion.git
cd RiftCompanion
npm ci
.\Lancer-Electron.cmd
```

Pour un dépôt déjà présent, fermer l’application puis exécuter à sa racine :

```powershell
git fetch origin
git switch codex/electron-migration
git pull --ff-only
npm ci
.\Lancer-Electron.cmd
```

Le lanceur compile l’application puis l’ouvre. En cas de modifications locales empêchant le changement de branche ou le pull, les conserver et résoudre le conflit avant de poursuivre.

## Utiliser l’exécutable portable

Ouvrir une exécution réussie du workflow [Electron and TypeScript](https://github.com/Kalamare/RiftCompanion/actions/workflows/electron.yml), sur la branche actuelle. Télécharger l’artefact `RiftCompanion-Electron-Windows`, extraire tout le ZIP puis lancer `RiftCompanion.exe`. Node.js et le SDK .NET ne sont pas nécessaires pour cet exécutable. Cette distribution de qualification n’est pas signée et ne se met pas à jour automatiquement.

## Connecter l’application au serveur

Dans **Réglages** :

1. Saisir l’adresse de l’API : `http://127.0.0.1:5080` pour la configuration locale par défaut. Si Docker publie un autre port, employer celui-ci. Un serveur distant exige HTTPS.
2. Coller la clé d’accès privée contenue dans `server/.secrets/access-key`, puis enregistrer. Il ne s’agit pas de la clé Riot.
3. Choisir si nécessaire le dossier contenant `LeagueClient.exe` et le rôle préféré.

Pour copier la clé d’accès sans l’afficher dans la console :

```powershell
(Get-Content -Raw .\server\.secrets\access-key).Trim() | Set-Clipboard
```

La clé est chiffrée par le stockage sécurisé du système et liée à l’adresse du serveur. Changer d’adresse peut nécessiter de la saisir à nouveau. La clé Riot reste dans le worker.

## Profil, historique et overlay

Au démarrage, le profil en cache apparaît puis se recharge automatiquement si l’accès serveur est configuré. Hors ligne, le cache reste consultable. **Mon profil** revient au propriétaire détecté par LoL ; rechercher quelqu’un d’autre ne remplace pas ce propriétaire.

La liste se complète automatiquement en approchant du bas ; **Voir plus de parties** permet aussi de demander la suite. Le serveur collecte les pages manquantes et prépare les suivantes. Les filtres portent sur l’historique demandé au serveur, pas seulement sur les dix cartes affichées. Les mises à jour conservent la position et les pages ouvertes. Un quota Riot peut différer la suite : la reprise est automatique et le cache reste visible. **Actualiser** garde son délai de cinq minutes.

Cette évolution modifie le client **et** le serveur. Après le `git pull`, reconstruire le backend avant de relancer Electron :

```powershell
docker compose -p rift-real --env-file server/.env -f server/compose.yaml -f server/compose.typescript.yaml up -d --build api worker
.\Lancer-Electron.cmd
```

Le bilan et les champions récents portent sur les vingt premières parties du filtre disponibles ; ouvrir d’autres pages ne change plus la taille de cet échantillon. Les statistiques de saison indiquent encore leur couverture partielle.

L’overlay se masque ou s’affiche avec **Alt+O**. Sa qualification actuelle concerne le mode sans bordure ; voir [ses limites](OVERLAY.md).

## Dépannage

Depuis la racine du dépôt :

```powershell
docker compose -p rift-real --env-file server/.env -f server/compose.yaml -f server/compose.typescript.yaml ps -a
docker compose -p rift-real --env-file server/.env -f server/compose.yaml -f server/compose.typescript.yaml logs --tail 80 api worker
Invoke-RestMethod http://127.0.0.1:5080/health
```

| Symptôme | Vérification |
|---|---|
| `ERR_CONNECTION_REFUSED` ou serveur inaccessible | Docker démarré, service API actif, port publié dans `ps` identique à celui des réglages. `127.0.0.1:5080->8080` signifie utiliser 5080. |
| « Accès serveur refusé : vérifie la clé » | L’API répond, mais la clé d’accès est refusée. Recopier `access-key`, enregistrer avec la bonne adresse. |
| `riot_access_denied` dans les logs du worker | Vérifier/renouveler la clé Riot côté serveur. Ce n’est pas la clé d’accès Electron. |
| Profil partiel ou historique épuisé | Vérifier le worker et les délais de quota ; la collecte au défilement fonctionne sans activer le backfill ; les statistiques ne couvrent que les parties collectées. |
| Avertissements `safeStorage.* is deprecated` | Dette de compatibilité Electron connue ; migration vers les méthodes asynchrones à faire. Ces avertissements ne sont pas une erreur d’authentification réseau. |
| Ancienne interface ou ancien comportement | Vérifier la branche, récupérer les modifications et relancer le build, ou remplacer le dossier portable par le nouvel artefact. |

`/health` confirme le mode du service (`runtime: typescript`, `data: riot`), pas la validité de la clé Riot. Ne partager ni contenu des fichiers secrets ni export complet de la configuration.

## Données locales

Le cache Electron se trouve dans `%APPDATA%\rift-companion`. Le profil personnel WPF peut être importé en lecture seule depuis `%LOCALAPPDATA%\RiftCompanion\personal-profile.json`. Les préférences SQLite ne sont pas importées automatiquement : reconfigurer le dossier LoL et le rôle si nécessaire.

Une mise à jour de l’interface seule ne nécessite pas de recréer le serveur. Les commandes d’exploitation et de retour arrière sont dans le [guide serveur](../server/README.md).
