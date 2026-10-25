# Migration Electron / TypeScript

L'application de bureau est Electron ; l'API et le worker sont des processus Node.js 24 en TypeScript. PostgreSQL reste partagé, sur le PC ou sur un serveur distant. **Aucun processus .NET n'est nécessaire pour ce nouveau parcours.** Les sources historiques restent disponibles pour comparaison et retour arrière.

## Tester sur Windows

Sur la branche `codex/electron-migration`, installer Node.js 24 LTS, puis à la racine :

```powershell
npm ci
npm run check
.\Lancer-Electron.cmd
```

Ou télécharger l'artefact `RiftCompanion-Electron-Windows` du workflow **Electron and TypeScript**, extraire entièrement le ZIP et lancer `RiftCompanion.exe`. Cette distribution de qualification n'est pas signée ; aucun SDK .NET n'est requis. Ne pas exécuter le programme depuis l'intérieur du ZIP.

Dans **Réglages**, saisir l'adresse du serveur (`http://127.0.0.1:5081` pour le PC) et sa clé d'accès existante, contenue dans `server/.secrets/access-key`. Ne pas utiliser la clé Riot dans l'application. Pour un serveur distant, HTTPS est obligatoire. Choisir si nécessaire le dossier contenant `LeagueClient.exe`.

La clé est chiffrée par `safeStorage` (DPAPI sous Windows), liée à l'adresse du serveur et n'est jamais relue par le renderer. La clé Riot reste exclusivement dans le worker. Le cache Electron est dans `%APPDATA%\rift-companion`. Le profil personnel WPF est importé en lecture seule depuis `%LOCALAPPDATA%\RiftCompanion\personal-profile.json`. Les anciens fichiers, la base SQLite et les secrets DPAPI WPF restent intacts. Reconfigurer le dossier LoL et le rôle préféré ; leur import SQLite n'est pas encore automatique.

## Passer le backend existant à Node.js

Les commandes suivantes supposent le projet réel `rift-real`, la configuration et les trois fichiers secrets existants. Vérifier le nom du projet avec `docker compose ls` avant d'utiliser un autre nom : le nom détermine le volume PostgreSQL. Ne jamais lancer `down -v`.

Depuis la racine, sauvegarder d'abord PostgreSQL. Pour éviter les redirections binaires de Windows PowerShell 5.1 :

```powershell
docker compose -p rift-real -f server/compose.yaml exec -T postgres pg_dump -U rift -d rift -Fc -f /tmp/rift-before-electron.dump
docker compose -p rift-real -f server/compose.yaml cp postgres:/tmp/rift-before-electron.dump ./rift-before-electron.dump
```

Conserver cette sauvegarde en dehors de Git. Ensuite :

```powershell
docker compose -p rift-real --env-file server/.env -f server/compose.yaml stop api worker
docker compose -p rift-real --env-file server/.env -f server/compose.yaml -f server/compose.typescript.yaml up -d --build api worker
Invoke-RestMethod http://127.0.0.1:5081/health
```

La santé doit afficher `runtime: typescript`, `data: riot`. Le port est celui de `RIFT_PORT` dans `server/.env` (5081 sur l'installation actuelle). Le schéma reste en v2, les matchs, agrégats, curseurs et quotas sont conservés. Aucun changement de mot de passe SQL et aucune suppression de volume. Ne pas laisser un ancien worker tourner en parallèle pendant la qualification.

Pour revenir aux services .NET sans modifier les données :

```powershell
docker compose -p rift-real --env-file server/.env -f server/compose.yaml stop api worker
docker compose -p rift-real --env-file server/.env -f server/compose.yaml up -d --build api worker
```

Le format des JSON écrits dans PostgreSQL reste compatible avec .NET. Les anciens clients WPF utilisent SignalR ; le nouveau serveur utilise des lectures de version toutes les 15 secondes dans Electron. Revenir au backend .NET pour utiliser entièrement le client WPF.

## Contrôles locaux

1. Rechercher `KalAram#ARAM` en EUW : identité, niveau, rangs et dix matchs progressifs.
2. Ouvrir une partie ; comparer K/D/A, CS, dégâts, or, objets et objectifs au client précédent.
3. Filtrer Solo, Flex, ARAM ; ouvrir les statistiques de saison (couverture partielle explicitement indiquée).
4. `Voir plus` consulte uniquement PostgreSQL. `Actualiser` respecte le délai de cinq minutes et le budget serveur.
5. Ouvrir LoL : **Mon profil** doit identifier le compte local. Rechercher quelqu'un d'autre puis redémarrer sans serveur : le propriétaire doit rester le compte LoL.
6. Vérifier la draft puis une partie d'entraînement en **sans bordure**. Activer l'overlay dans les réglages ; `Alt+O` le masque/affiche, les clics traversent la fenêtre.
7. Fermer l'application : l'overlay et les lectures locales s'arrêtent.

L'overlay Electron standard **ne règle pas le plein écran exclusif**. Il présente actuellement le roster ; la présentation complète des statistiques par joueur et les éléments graphiques avancés WPF restent à reprendre. Le moteur plein écran (indépendant ou Overwolf approuvé) doit être qualifié séparément avec LoL/Vanguard.

## Développement et vérifications

- `apps/desktop` : main, preload isolé et renderer TypeScript ; aucun Node.js dans la page.
- `apps/server` : API HTTP et worker durable ; `packages/core` : modèles et calculs communs.
- `npm run check` : typage, tests métier/cache, compilation.
- `npm run test:desktop` : Electron réel, serveur synthétique, chiffrement Windows, recherche, détail et redémarrage hors ligne. À lancer sur Windows avec une session graphique.
- `RIFT_TEST_DATABASE_URL` + `npm run test:integration` : PostgreSQL de test ; chaque exécution utilise et supprime uniquement son propre schéma `rift_check_*`.
- `npm run package:win` : distribution Windows portable.
- `npm run api` / `npm run worker` : développement Node.js avec `PGHOST`, `PGUSER`, `PGDATABASE`, `PGPASSWORD` ou `PGPASSWORD_FILE`, `RIFT_ACCESS_KEY_FILE`, `RIOT_API_KEY_FILE`. `HOST` est local par défaut. `RIFT_FIXTURE_MODE=true` est réservé aux tests synthétiques.

## Avant une publication publique

Le serveur distant privé peut être déployé derrière un reverse proxy HTTPS, avec sauvegardes et secrets opérateur. Le mode actuel utilise une clé privée partagée et des budgets globaux : ce n'est pas une authentification pour un public multiutilisateur. Prévoir comptes/sessions, quotas par utilisateur, supervision, migrations de production, distribution signée et mises à jour. La clé Riot de développement n'offre ni les quotas ni l'autorisation d'une exploitation publique ; les démarches Riot et, si choisi, Overwolf restent distinctes.

## État de transition

Le nouveau parcours profil → collecte → statistiques → détails → cache → client LoL fonctionne sans .NET et fait l'objet de tests automatisés. Le rendu n'est pas une reproduction pixel à pixel de WPF. Restent notamment : graphiques de rangs et de rôles avancés, infobulles illustrées des runes/objets, statistiques du roster overlay, import des préférences SQLite, moteur plein écran et mesures de performances en jeu. Ne retirer `native/` et les anciens services qu'après validation de ces écarts ; les assets historiques sont encore réutilisés à la compilation.
