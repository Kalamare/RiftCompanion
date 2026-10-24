# Tests cloud — reprise du 24 octobre 2026

Base : v41, commit `bad1caa`. Nouvelle base de test synthétique ; aucune
récupération de données de l’ancien PC et aucune collecte Riot réelle.

## Configuration de l’environnement

Le nouveau chat reçoit bien `centralus.data.mcr.microsoft.com` dans la politique
réseau de démarrage. Le téléchargement de `mcr.microsoft.com/dotnet/sdk:10.0`
a réussi, contrairement à la session précédente. Le statut de readiness réseau
reste `unknown` dans l’outil de gestion : la réussite est établie par le test réel.

Les constructions Docker utilisent le proxy configuré par la plateforme et le
bundle CA via un secret BuildKit temporaire. La vérification TLS reste active.
`server/compose.cloud-test.yaml` impose les données synthétiques et désactive le
backfill pour API, worker et contrôles. Aucun secret Riot réel n’est utilisé.

La compilation a également révélé NU1903 pour SignalR 10.0.12
([GHSA-334f-vvp4-7g6p](https://github.com/advisories/GHSA-334f-vvp4-7g6p)).
La référence client passe à 10.0.13, version corrective indiquée par l’avis,
avec régénération des fichiers de dépendances verrouillées concernés.

## Exécution

Initialiser les fichiers locaux ignorés `server/.secrets/postgres-password`
et `access-key` avec des valeurs aléatoires, et `riot-key` vide.
Ils doivent être lisibles par les utilisateurs des conteneurs ; le répertoire
parent reste privé. Ne pas remplacer les secrets d’un backend existant.

Depuis la racine du dépôt :

```sh
docker compose -p rift-cloud-test -f server/compose.yaml -f server/compose.cloud-test.yaml --profile checks build
docker compose -p rift-cloud-test -f server/compose.yaml -f server/compose.cloud-test.yaml --profile checks run --rm checks
docker compose -p rift-cloud-test -f server/compose.yaml -f server/compose.cloud-test.yaml up -d api worker
# Attendre que http://127.0.0.1:5080/health réponde avec data: synthetic.
docker compose -p rift-cloud-test -f server/compose.yaml -f server/compose.cloud-test.yaml --profile checks run --rm clientchecks
```

Ne pas lancer ces tests contre un backend réel. Les contrôles client refusent
un endpoint qui n’annonce pas explicitement des données synthétiques.
La validation WPF nécessite toujours Windows.

## Résultats

- Compilation des quatre images : réussie, restauration verrouillée active.
- 40 contrôles PostgreSQL : réussis.
- REST/SignalR : authentification, validation des entrées, notifications,
  statistiques, profils, détails, reconnexion et cooldown réussis.
- Dépendances Windows restaurées ; contrôles WPF non exécutés sous Linux.
- Conteneurs arrêtés après validation ; volume synthétique conservé.

Le conteneur `clientchecks` partage la boucle locale du conteneur API, via
`network_mode: service:api`, pour respecter la restriction HTTP local du client.
Pour rejouer la suite, utiliser un nouveau nom de projet Compose et une base
synthétique vide : les tests attendent une première collecte et sa notification.

La branche de publication est `codex/cloud-backend-handoff`. Le workflow
`Windows native checks` compile le client et exécute les contrôles métier/WPF
sur Windows, puis fournit l’artefact `RiftCompanion-Windows` pendant sept jours.
Les tests synthétiques ne nécessitent aucune clé Riot.

## Passage au réel

Le 24 octobre, le secret configuré est signalé `ready` et la politique réseau
`enforced`, mais l’appel de validation EUW retourne HTTP 403. La validation réelle
reste bloquée jusqu’au renouvellement ou à la vérification des droits de la clé.
Ne pas confondre disponibilité du secret et autorisation de l’API Riot.
Aucune collecte de profil réel n’a été lancée. Ne jamais publier de clé dans Git.

Le choix de l’hébergement et du domaine reste à préciser avant un déploiement
durable. L’environnement de test actuel ne fournit pas à lui seul un hébergement
permanent accessible depuis le client Windows.
