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

Le 24 octobre, le secret est signale `ready` et la politique reseau `enforced`.
Le HTTP 403 a ete identifie comme une page Cloudflare « Sorry, you have been
blocked », et non comme une erreur JSON d'authentification Riot. Le renouvellement
du secret n'a pas leve ce blocage. La validite de la cle reste donc non etablie
depuis cette sortie reseau. Aucune collecte de profil reel n'a ete lancee.

Choix utilisateur : backend prive sur son PC. Suivre
[INSTALLATION-PC.md](INSTALLATION-PC.md) et `Lancer-Backend-Local.cmd`.
Aucun domaine ni exposition Internet necessaire pour ce mode local.
