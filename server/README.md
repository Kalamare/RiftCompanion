# Backend privé Node.js / PostgreSQL

Le backend actif est l’API et le worker TypeScript dans `apps/server`. Les fichiers Compose se combinent : **toujours ajouter `server/compose.typescript.yaml` pour le parcours Electron**. `compose.yaml` seul démarre les services .NET historiques.

Les commandes ci-dessous partent de la racine du dépôt et du projet Compose `rift-real`. Pour une installation existante, vérifier son nom avec `docker compose ls` : il détermine le volume PostgreSQL. Le port par défaut est **5080**, configurable dans `server/.env`. Le PC et Docker doivent rester allumés pour servir les données.

## Première installation

Installer Docker Desktop et le démarrer en mode conteneurs Linux. Initialiser uniquement les fichiers manquants :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File server/Initialize-Dev.ps1
```

Ce script conserve les fichiers existants. Sur une installation neuve, renseigner `server/.secrets/riot-key` avec la clé du portail Riot Developer, sans la publier. Les secrets locaux sont :

| Fichier | Usage |
|---|---|
| `server/.secrets/riot-key` | Authentification auprès de Riot, utilisée par le worker |
| `server/.secrets/access-key` | Accès privé à notre API, à saisir dans Electron |
| `server/.secrets/postgres-password` | Mot de passe PostgreSQL, conservé avec le volume existant |

Vérifier dans `server/.env` : `RIFT_FIXTURE_MODE=false`, `RIFT_PORT=5080`, `RIFT_HISTORY_BACKFILL=false`. Une variable définie dans la session PowerShell prend priorité sur le fichier `.env` ; ne conserver aucun ancien override involontaire.

```powershell
docker compose -p rift-real --env-file server/.env -f server/compose.yaml -f server/compose.typescript.yaml up -d --build api worker
Invoke-RestMethod http://127.0.0.1:5080/health
```

Attendu : `status: up`, `runtime: typescript`, `data: riot`. Adapter l’URL si `RIFT_PORT` diffère. L’API est liée à `127.0.0.1` ; PostgreSQL n’est pas publié sur le réseau hôte.

## Passer d’un backend .NET existant à Node.js

Sauvegarder d’abord la base dans un fichier hors Git. La copie évite les redirections binaires de Windows PowerShell 5.1 :

```powershell
docker compose -p rift-real --env-file server/.env -f server/compose.yaml exec -T postgres pg_dump -U rift -d rift -Fc -f /tmp/rift-before-electron.dump
docker compose -p rift-real --env-file server/.env -f server/compose.yaml cp postgres:/tmp/rift-before-electron.dump ./rift-before-electron.dump
docker compose -p rift-real --env-file server/.env -f server/compose.yaml stop api worker
docker compose -p rift-real --env-file server/.env -f server/compose.yaml -f server/compose.typescript.yaml up -d --build api worker
```

Le schéma PostgreSQL reste en v2 et les JSON persistants restent compatibles avec les services historiques. Les matchs, agrégats, curseurs et quotas sont conservés. Ne pas faire tourner un ancien worker en parallèle et ne pas utiliser `down -v` sur la base réelle.

## Mises à jour et arrêt

Après récupération des sources, reconstruire les services seulement si leur code a changé :

```powershell
docker compose -p rift-real --env-file server/.env -f server/compose.yaml -f server/compose.typescript.yaml up -d --build api worker
```

Pour arrêter sans supprimer les données :

```powershell
docker compose -p rift-real --env-file server/.env -f server/compose.yaml -f server/compose.typescript.yaml stop
```

## Historique et clé Riot

Par défaut, le worker demande les **dix matchs récents**. « Voir plus » consulte uniquement les données présentes en base ; les anciennes collectes et les contributions de participants peuvent fournir davantage de matchs.

Pour collecter depuis `RIFT_HISTORY_FROM`, mettre `RIFT_HISTORY_BACKFILL=true` dans `server/.env`, puis appliquer :

```powershell
docker compose -p rift-real --env-file server/.env -f server/compose.yaml -f server/compose.typescript.yaml up -d api worker
```

Ouvrir ensuite le profil pour prolonger son activité. Le remplissage est progressif, soumis aux quotas et à l’historique accessible. Le backfill actuel demande des pages Riot de 20 identifiants, distinctes des pages d’affichage de 10. Il ne précharge pas automatiquement toute la population EUW.

Pour renouveler la clé Riot, modifier `server/.secrets/riot-key`, puis recréer les services afin qu’ils relisent les secrets :

```powershell
docker compose -p rift-real --env-file server/.env -f server/compose.yaml -f server/compose.typescript.yaml up -d --force-recreate api worker
```

Une pause d’accès refusé déjà persistée peut durer quinze minutes. Les codes et limites sont décrits dans [Données](../docs/DONNEES.md).

## Retour au backend .NET

```powershell
docker compose -p rift-real --env-file server/.env -f server/compose.yaml -f server/compose.typescript.yaml stop api worker
docker compose -p rift-real --env-file server/.env -f server/compose.yaml up -d --build api worker
```

Pour utiliser le client WPF et SignalR, suivre ensuite [native/README.md](../native/README.md). Ce retour ne supprime pas les données.

## Serveur distant et tests

Un serveur distant privé exige HTTPS, une exposition réseau configurée explicitement et des sauvegardes testées. Le Compose local ne publie l’API que sur loopback. Une clé partagée et des budgets globaux ne suffisent pas pour une offre publique multiutilisateur : voir [Architecture](../docs/ARCHITECTURE.md).

Les commandes de tests Node/PostgreSQL sont dans [Validation](../docs/validation.md). Les profils Compose `checks` et `compose.cloud-test.yaml` appartiennent au parcours .NET historique, pas à la suite TypeScript.
