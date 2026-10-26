# Validation

- 27/10/2026 — Défilement pendant la collecte : Chromium vérifie que le conteneur de l’historique reste le même et n’est jamais détaché lors du chargement de nouvelles pages ; position conservée à 500 px (30 parties) et 4 000 px (90 parties virtualisées). Compilation TypeScript, build et 18 tests unitaires réussis. Test avec données synthétiques, à confirmer sur le profil réel sous Windows.

## Synchronisation progressive — vérification actuelle

Le parcours de synchronisation a été vérifié localement sur une base PostgreSQL 17 dédiée et des données entièrement synthétiques :

- 18 tests domaine/cache/logique UI, typage et compilation réussis.
- 16 contrôles PostgreSQL : migration additive, ancien format, trente consultations simultanées mutualisées, checkpoints, quota/reprise, détails manquants, pages source décalées sans doublon, rotation des profils, admission saturée à 1 000 sessions avec cache toujours lisible, isolation des quotas par méthode et réservation atomique sans consommation en cas de refus.
- Chromium : cache puis chargement automatique partiel, poursuite d’une page initialement absente, filtres serveur, bilan indépendant des pages, défilement automatique, position conservée et DOM virtualisé sur un historique de 120 parties synthétiques. Les contrôles de disposition, détails, runes et overlay sont conservés.

La [CI de la branche](https://github.com/Kalamare/RiftCompanion/actions/workflows/electron.yml) rejoue ces tests et les contrôles Electron Windows depuis les sources puis l’exécutable portable. Utiliser une exécution réussie correspondant au commit récupéré. La preuve Windows antérieure, avant ce changement de collecte, est [adcd1f9 / exécution 40224905591](https://github.com/Kalamare/RiftCompanion/actions/runs/40224905591).

La recette de cette nouvelle collecte sur KalAram#ARAM reste à réaliser. Ces contrôles ne sont ni une mesure de charge de milliers d’utilisateurs, ni un test des quotas réels Riot, ni une mesure des FPS dans LoL.

## Rejouer

Depuis la racine, avec Node.js 24 :

```powershell
npm ci
npm run check
npx playwright install chromium
npm run test:ui
```

Sur Windows avec session graphique :

```powershell
npm run test:desktop
npm run package:win
```

Les contrôles PostgreSQL demandent une connexion à une base de test dédiée dans `RIFT_TEST_DATABASE_URL`, puis `npm run test:integration`. Chaque exécution crée et supprime son propre schéma `rift_check_*`. La CI décrit la configuration reproductible dans [.github/workflows/electron.yml](../.github/workflows/electron.yml).

Le mode `RIFT_FIXTURE_MODE=true` est réservé aux données synthétiques, avec projet/volume distinct du réel. Les contrôles .NET/WPF et bancs DirectX restent des suites séparées ; leurs anciens résultats ne qualifient pas Electron.

## Recette sur le PC

1. Démarrer le backend et vérifier `/health` sur le port effectivement publié.
2. Démarrer Electron : cache immédiat, puis chargement automatique de KalAram#ARAM sans appuyer sur Actualiser.
3. Comparer niveau/rangs et quelques parties ; tester filtres, objets, runes et navigation vers un participant.
4. Faire défiler plusieurs pages au-delà des parties déjà stockées ; vérifier collecte automatique, reprise après quota, filtre Solo/ARAM, bilan stable et position conservée. Relancer le worker pendant une page en cours, puis vérifier la reprise sans doublon. Ne pas effacer de volume.
5. Rechercher un autre compte puis revenir à Mon profil ; redémarrer sans serveur et vérifier le propriétaire en cache.
6. En partie d’entraînement sans bordure, tester Ctrl+X, clics traversants, Alt-Tab, transitions et fermeture.
7. Vérifier l’arrêt des processus et les performances sur une session prolongée.

## Mesures en jeu à consigner

Même machine, résolution, scène et réglages ; trois passages par scénario. Comparer LoL seul, client ouvert, overlay masqué puis visible. Relever CPU/RAM de tous les processus Electron, GPU, FPS/1 % low, temps de frame p95/p99 et trafic réseau. Répéter navigation et reconnexions pendant 60 minutes pour repérer croissance mémoire ou multiplication des requêtes.

Les budgets restent à fixer sur une machine de référence. Un diagnostic fermé fait partie du scénario normal ; son coût doit être mesuré séparément.
