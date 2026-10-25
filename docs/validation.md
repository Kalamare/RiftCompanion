# Validation

## Dernière preuve automatisée

Commit applicatif [adcd1f9](https://github.com/Kalamare/RiftCompanion/commit/adcd1f98e4a870a37c6fc61731b1f82e2a63125d), [CI du 25 octobre 2026](https://github.com/Kalamare/RiftCompanion/actions/runs/40224905591) :

- 18 tests domaine/cache/logique UI et typage/compilation réussis.
- 12 contrôles d’intégration PostgreSQL réussis.
- Contrôles Chromium : disposition à 1220/1050 px, filtres, objets, détails, runes et grille d’overlay ; démarrage automatique, pagination 10→20→30→35 avec mise à jour intermédiaire, cache hors ligne.
- Tests Electron réels sous Windows depuis les sources et depuis l’exécutable portable : isolation du renderer, secret chiffré, recherche, détails, récupération après erreur et redémarrage hors ligne.

Les captures utilisent des données synthétiques. L’utilisateur a constaté le lancement et les données de KalAram#ARAM ; les derniers correctifs de démarrage/pagination attendent sa recette réelle. Ces résultats ne mesurent pas les FPS dans LoL.

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
4. Ajouter plusieurs pages si disponibles ; vérifier fin explicite, défilement et maintien de la liste lors d’une mise à jour.
5. Rechercher un autre compte puis revenir à Mon profil ; redémarrer sans serveur et vérifier le propriétaire en cache.
6. En partie d’entraînement sans bordure, tester Alt+O, clics traversants, Alt-Tab, transitions et fermeture.
7. Vérifier l’arrêt des processus et les performances sur une session prolongée.

## Mesures en jeu à consigner

Même machine, résolution, scène et réglages ; trois passages par scénario. Comparer LoL seul, client ouvert, overlay masqué puis visible. Relever CPU/RAM de tous les processus Electron, GPU, FPS/1 % low, temps de frame p95/p99 et trafic réseau. Répéter navigation et reconnexions pendant 60 minutes pour repérer croissance mémoire ou multiplication des requêtes.

Les budgets restent à fixer sur une machine de référence. Un diagnostic fermé fait partie du scénario normal ; son coût doit être mesuré séparément.
