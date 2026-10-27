# Feuille de route

État du 27 octobre 2026, branche `codex/electron-migration`. [Tableau Notion](https://app.notion.com/p/f81e9e8403b6448c97790c6b1a23007e) pour les tâches ; ce fichier décrit l’ordre et les critères de passage.

## Disponible en qualification

Client Electron, API/worker Node.js, PostgreSQL compatible avec les données existantes, interface portée depuis WPF, profils/détails/runes, statistiques partielles de saison, cache hors ligne et overlay de dix joueurs. La synchronisation progressive, la collecte au défilement, les filtres serveur, les sessions partagées et le bilan récent indépendant des pages sont implémentés ; la recette du dernier correctif sur le PC utilisateur reste à confirmer.

## 1. Stabiliser la version personnelle

- Valider KalAram#ARAM : démarrage sans clic, filtres, plusieurs pages, fin d’historique, déconnexion et reprise.
- Vérifier le défilement, le changement de compte pendant un chargement, la fermeture et le retour hors ligne.
- Remplacer les méthodes `safeStorage` dépréciées par leurs variantes asynchrones, avec lecture des secrets existants et tests Windows.
- Porter les préférences SQLite utiles et améliorer les diagnostics de connexion sans exposer les secrets.
- Qualifier fenêtres, focus, multi-écran/DPI et overlay sans bordure/fenêtré : Ctrl+X, pseudos cliquables et clics traversants.

Critère : parcours reproductible sur le PC utilisateur, erreurs compréhensibles et aucune régression de données ou d’interface.

## 2. Compléter et qualifier la collecte

- Recetter les pages manquantes et la reprise après quota sur KalAram, puis mesurer latence, attente en file, taux de cache et fraîcheur sur plusieurs profils.
- Ajouter bilans 30 jours/périodes, couverture et collecte de statistiques indépendantes du défilement, sans présenter les agrégats partiels comme complets.
- Ajouter profils suivis et signal de fin de partie avec déduplication et admission serveur.
- Définir rétention/compaction des sessions, observabilité et migration versionnée de l’extension SQL.
- Avant le public : remplacer la clé partagée par des comptes, établir les budgets/équité par compte, puis tester charge et pannes avec les quotas Riot réellement alloués.

## 3. Stabiliser l’overlay sans bordure/fenêtré et mesurer les performances

Comparer LoL seul, application ouverte, overlay masqué et visible : trois passages comparables, CPU/RAM de tous les processus Electron, GPU, temps de frame p95/p99, 1 % low et endurance 60 minutes. Fixer les budgets à partir de la machine de référence, sans annoncer d’impact nul.

Décision du 27/10/2026 : abandon du plein écran exclusif. L’overlay reste une fenêtre Electron indépendante, sans Overwolf ni Game Bar. Les essais GOverlay/DirectX sont archivés et ne font plus partie de la feuille de route. Priorité à la visibilité, aux interactions, au focus et au placement sur les écrans dans les deux modes retenus. Voir [Overlay](OVERLAY.md).

## 4. Enrichir le profil sans inventer des données

- Observations LP : table additive par joueur/file/saison avec date, tier/division/LP et provenance. Réutiliser les réponses de rang déjà obtenues, dédupliquer, afficher les trous et ne pas attribuer arbitrairement un delta à un match.
- Graphique LP : lectures bornées 7/30/90 jours, promotions/apex/saisons testés, aucun passé reconstruit avant les premières observations.
- Bilans quotidiens, joueurs rencontrés, filtres champion et détails supplémentaires à partir des matchs disponibles.
- Variantes Arena : placements réels et agrégats compatibles, sans les déduire du seul booléen victoire.
- Calendrier de saison explicite et partagé entre collecte et interface.

## 5. Préparer le serveur distant puis le public

Le serveur privé distant est une étape intermédiaire, pas un service public prêt à ouvrir. Prévoir HTTPS, sauvegardes restaurées en test, comptes/sessions, quotas individuels, supervision, migrations, confidentialité/rétention, signature et mises à jour du client. Vérifier l’enregistrement du produit, les droits des sources et la clé Riot adaptée.

Le préchargement EUW reste une proposition : population, volume, budget et fraîcheur bornés, priorité aux consultations, curseurs durables et métriques du taux de cache. Les pages de saison de 100 identifiants sont une optimisation à étudier ; le code actuel utilise 20. Pas de dataset externe importé ni d’exploration massive active.

## 6. Revenir aux aides de draft et d’équipement

Après stabilisation : pool de champions, caractéristiques éditoriales sourcées, recommandations expliquées et évaluation sur des drafts annotées. Les données de matchups nécessitent un corpus autorisé et une validation séparée.

Pour l’équipement live, établir d’abord quelles informations sont effectivement visibles, puis comparer la valeur des objets sans inventer l’or total ni révéler des informations cachées. L’inventaire de fin de match ne prouve pas la disponibilité de l’inventaire live.
