# Backend Rift Companion — première tranche fonctionnelle

**v34 : collecte bornée aux 20 dernières parties par défaut**, sans rattrapage de saison. Les statistiques du profil utilisent l’échantillon affiché. Les anciennes pages stockées restent consultables sans appels Riot. `RIFT_HISTORY_BACKFILL=true` réactive explicitement le comportement historique décrit plus bas ; ne pas l’activer pour le mode économe actuel. Voir [les règles v34](../docs/ACTUALISATION-QUOTAS.md).

API ASP.NET Core, worker .NET/Quartz, PostgreSQL 17, conteneurs Linux et client REST/SignalR. Le mode serveur natif est disponible en v31, avec conservation du mode local pendant la migration. Ce déploiement est **privé et local**, pas une release publique.

## Essayer sans clé Riot

Prérequis : SDK .NET 10 compatible avec `global.json`, Docker Desktop démarré avec les conteneurs Linux. Depuis la racine du dépôt :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File server/Initialize-Dev.ps1 -Demo
docker compose -f server/compose.yaml up -d --build
powershell -NoProfile -ExecutionPolicy Bypass -File native/Build.ps1 -Check
.\Lancer-Serveur-Local.cmd
```

Rechercher **Alice#TEST** sur EUW, puis **Bob#TEST**. Les deux profils partagent une même partie fictive à dix participants, datée du 1er février 2026. Vérifier une période commençant avant cette date. Le second profil réutilise le match déjà enregistré. Aucun appel Riot ou OP.GG n’est effectué par le backend en mode fictif ; les images du client peuvent encore utiliser le CDN public Riot.

Le script conserve toute configuration déjà présente : si `server/.env` existe, vérifier que `RIFT_FIXTURE_MODE=true` pour cet essai. Ne pas mélanger données fictives et réelles dans la même base : utiliser un autre nom de projet Compose (`-p rift-real`) pour le réel. Le nom du projet sépare aussi son volume PostgreSQL.

L’API écoute uniquement sur `127.0.0.1:5080`. PostgreSQL n’expose aucun port sur l’hôte. Les mots de passe générés sont dans `server/.secrets/`, exclus de Git et du contexte de construction Docker. La clé de développement de notre API n’est pas une clé Riot et ne doit jamais être embarquée dans une distribution publique.

Pour arrêter les conteneurs en conservant la base :

```powershell
docker compose -f server/compose.yaml down
```

`Lancer-Natif.cmd` conserve le fonctionnement local habituel. `Lancer-Serveur-Local.cmd` configure l’API locale uniquement pour le processus lancé. Pour un serveur privé distant, définir `RIFT_SERVER_URL` en HTTPS et `RIFT_SERVER_ACCESS_KEY_FILE` avant de lancer le client. Ce mécanisme d’accès partagé est destiné au développement privé.

## Utiliser Riot côté serveur

1. Mettre la clé autorisée dans `server/.secrets/riot-key`, sans la committer.
2. Choisir `RIFT_FIXTURE_MODE=false` et une date UTC de début dans `server/.env`.
3. Démarrer un projet Compose distinct de celui des données fictives.

Le client en mode serveur n’affiche plus le champ clé Riot ni la synchronisation manuelle. Il charge les profils, les pages d’historique, les fiches de partie et les statistiques calculées par le serveur. Les trois filtres statistiques restent indépendants. Une notification SignalR provoque une relecture regroupée ; une vérification par minute lorsque la vue est visible permet de réconcilier l’état après une interruption. Les contrôles LoL, l’overlay, les images et leur cache restent locaux.

OP.GG n’est appelé ni par ce backend ni par l’enrichissement du profil/overlay lorsque ce mode est actif. Les positions mondial/serveur ne sont donc pas fournies dans ce mode ; aucun classement n’est inventé. Les rangs Riot actuels (palier, division, LP, victoires/défaites) sont datés indépendamment de l’historique. Un participant dont le rang n’a pas été demandé a un rang inconnu, pas « non classé ».

## Collecte et mutualisation

- Une recherche crée ou réactive un job durable. L’intérêt expire 30 minutes après la dernière consultation ou l’abonnement de la page visible. Le job récent vérifie les nouveautés toutes les 15 minutes tant que le profil reste actif, sous réserve des quotas et de la disponibilité de Riot ; le parcours de saison terminé ne redémarre pas.
- Quartz 4.1.1 déclenche un passage toutes les cinq secondes. Son déclencheur est en mémoire ; **les jobs, curseurs, échéances, baux et reprises sont persistés dans nos tables PostgreSQL**, pas dans un JobStore Quartz. Un verrou PostgreSQL n’autorise qu’un collecteur à travailler. Il ne faut pas augmenter son nombre pour contourner les quotas.
- Le premier parcours commence à `RIFT_HISTORY_FROM`, en pages de 20 IDs, avec une borne supérieure stable. Les nouvelles identités sont prioritaires sur le rattrapage historique. Une attente de quota libère le collecteur ; la reprise réutilise les matchs déjà stockés. Le garde-fou est de 10 000 IDs par parcours ; un dépassement reste signalé comme partiel.
- Une consultation ne modifie pas l’échéance Riot. Une actualisation explicite est soumise au délai partagé (5 minutes par défaut) et au budget privé (5 demandes acceptées/10 minutes). Elle ne relance que le job récent, indépendamment du curseur de saison. Les nouvelles recherches ont un budget de 3/10 minutes. Voir [Actualisation et quotas](../docs/ACTUALISATION-QUOTAS.md) pour la migration v2 et les limites restantes. L’identité est disponible avant la fin de la collecte et chaque match enregistré produit une notification ; les 20 parties restent la fenêtre initiale par défaut.
- Les passages suivants chevauchent les 48 dernières heures de la fenêtre précédente, notamment pour les parties terminées ou indexées tardivement. Un retard supérieur à cette fenêtre demande un rattrapage explicite côté serveur ; il n’existe pas de garantie de données exhaustives ou instantanées.
- Chaque détail inconnu est téléchargé puis enregistré **une seule fois par région/ID**. Le match, ses contributions, les agrégats de tous ses participants et leurs notifications sont écrits dans la même transaction. Un rejeu après arrêt ne double pas les compteurs.
- Les joueurs découverts dans ces matchs ne créent **aucun job de suivi**. Ils possèdent des statistiques partielles issues des rencontres connues. Une consultation explicite, y compris une carte chargée dans l’overlay, peut ensuite déclencher leur propre parcours. Aucune expansion récursive du graphe des joueurs.
- Les indisponibilités de détails sont mémorisées par job/ID, sans additionner plusieurs fois le même 404. « Historique accessible parcouru » signifie que la liste disponible a été parcourue, jamais que Riot a restitué toutes les parties de la saison.
- Quotas conservateurs de 18 requêtes/s et 90/2 min par hôte, réservations persistantes, limites plus basses observées dans les en-têtes, pauses partagées sur `429`/`Retry-After` et erreurs d’accès. Les limites de méthode sont appliquées à tout l’hôte dans cette première version : c’est volontairement conservateur.

## Données et calculs

`Rift.Contracts` partage les DTO ; `Rift.Core` conserve les règles de domaine. Le client n’importe pas les centaines de détails nécessaires aux bilans de saison.

Les agrégats quotidiens sont indexés par joueur, file, champion et rôle. On conserve les sommes des kills, morts, assists, CS, dégâts, or, vision, balises et durées. Les ratios temporels utilisent la durée cumulée ; le KDA est `(kills + assists) / max(1, morts)`. La participation est la moyenne des ratios par partie applicables, avec un compteur distinct. Les remakes sont exclus, l’Arène n’a pas de participation d’équipe classique, les files sans rôles standards n’alimentent pas le classement des rôles. Les détails normalisés sont conservés pour permettre un recalcul futur ; l’outil de recalcul versionné reste à ajouter.

Les requêtes statistiques utilisent des journées UTC, borne de fin exclusive, maximum trois ans. `queue=0` : tout ; `-1` : Solo/Duo + Flex ; sinon ID exact, notamment `420`, `440`, `450`, `1700`. Choisir une période antérieure à la collecte n’étend pas automatiquement la collecte et garde l’indicateur de couverture partielle.

API `/v1`, en-tête privé `X-Rift-Key` :

| Route | Résultat |
|---|---|
| `GET /health` | Vivacité et mode réel/fictif, sans authentification |
| `POST /v1/profiles/lookup` | `{platform, riotId}` → ticket durable |
| `GET /v1/lookups/{id}` | État, erreur normalisée, identité connue |
| `GET /v1/players/{platform}/{puuid}` | Métadonnées, rang daté, version et couverture |
| `GET /v1/players/{platform}/{puuid}/stats?from=…&until=…&queue=…` | Bilan, champions et rôles agrégés |
| `GET /v1/players/{platform}/{puuid}/profile?start=0&count=20&end=0` | Profil et historique récent paginé, 50 maximum par page |
| `GET /v1/matches/{platform}/{id}` | Détail déjà enregistré, sans collecte implicite |
| `/v1/events` | SignalR : `Watch(platform, puuid)`, événement `ProfileChanged` |

Les notifications ne sont pas une garantie de livraison : le client relit les données au démarrage/rebranchement et périodiquement. Les écritures sont sérialisées dans cette tranche pour préserver l’ordre de commit de l’outbox ; les lecteurs statistiques utilisent une transaction cohérente.

## Vérifier

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File server/Check.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File native/Build.ps1 -Check
```

Le premier script crée un projet Docker au nom aléatoire, un port local libre et un volume de test distincts. Il teste PostgreSQL, démarre API/worker, vérifie le véritable client REST/SignalR, puis supprime **uniquement** ce projet temporaire et son volume. Aucune clé Riot nécessaire. Les tests vérifient notamment le partage des dix contributions, l’absence de suivi récursif, les écritures concurrentes/reprises, l’atomicité en cas d’erreur, les formules pondérées, les filtres, les quotas, l’authentification et les notifications. Les vérifications WPF couvrent aussi l’affichage des agrégats serveur sans les remplacer par les derniers matchs locaux.

## Avant une bêta publique

Cette tranche n’est pas un service prêt à exposer sur Internet. Restent : authentification par session et limitation par utilisateur, approbation Riot/clé de production, TLS et gestion opérationnelle des secrets, migrations versionnées complètes, sauvegarde/restauration, purge/rétention et droits des personnes, observabilité/alertes serveur et tests de charge. Le schéma initial refuse une version future ; il ne constitue pas encore un système complet de migrations.

Il n’y a actuellement **aucune purge des matchs, profils ni notifications**. Le volume persistant croît : réserver ce déploiement au développement jusqu’à l’ajout de la politique de conservation. Les alias après changement de Riot ID peuvent encore créer deux jobs pour un même PUUID ; les contributions restent dédupliquées. Une navigation depuis un ancien Riot ID qui désigne désormais un autre compte est refusée.

Le cache hors ligne persistant des agrégats serveur et la conversion des statistiques spécifiques de l’overlay en modèles entièrement précalculés restent à compléter. Les petits indicateurs de ses cartes sont encore calculés à partir de leur historique récent chargé ; la saison du profil est calculée côté serveur. L’ancien code d’accès local reste livré pour la transition privée et devra être retiré de la distribution publique avec les dépendances externes non autorisées.

Aucun gain de FPS n’est revendiqué par ces tests. Mesurer séparément le rendu WPF/overlay et les performances serveur avant/après. Docker sur le PC de test consomme naturellement des ressources ; en distribution, le serveur sera hébergé ailleurs.
