# Profil : graphiques et prochaines étapes

## Livré en v37

Référence inspectée dans le navigateur : https://www.deeplol.gg/summoner/euw/KalAram-ARAM (24 septembre 2026).

- Anneau bleu/rouge pour les victoires/défaites du bilan filtré, état neutre sans partie.
- Moyennes K/D/A colorées, distinctes du ratio KDA global pondéré par les morts.
- Barres verticales victoires/défaites par rôle, même échelle de nombres de parties, ordre Top/Jungle/Mid/ADC/Support. Infobulles : effectif, V/D, winrate. Modes sans rôle standard exclus ; rôles inconnus signalés.
- Route des divisions pour Solo et Flex : division courante soulignée, LP actuels et distance au prochain seuil. Les paliers précédents représentent l’ordre des divisions, pas des promotions observées. Maître/Grand maître/Challenger ont une présentation sans divisions ni seuil de promotion fixe inventé.
- Composants vectoriels WPF, sans dépendance graphique, timer, animation permanente ou requête supplémentaire. Les graphiques du mode serveur suivent les filtres indépendants et la pagination déjà existants.

## 1. Historique LP côté serveur — priorité suivante

Créer une table `rank_observations` par plateforme, PUUID, file et saison : date d’observation UTC, tier, division, LP, victoires/défaites et provenance. Enregistrer les réponses de classement déjà récupérées par le collector ; ne pas ajouter une boucle de polling dédiée. Séparer les files Solo/Flex et ne pas enregistrer une erreur réseau comme un déclassement.

Dédupliquer les relevés identiques en conservant première et dernière observation. Index sur joueur/file/date. Migration additive et tests de concurrence/idempotence. Enregistrer les données réellement observées à partir de l’activation : la base actuelle n’a pas de série LP historique permettant de reconstruire les 30 jours passés.

## 2. API et graphique LP

Endpoint authentifié de lecture, fenêtres bornées 7/30/90 jours, nombre de points plafonné, cache et version de données. Utiliser les notifications déjà présentes pour invalider le cache. Agrégation serveur pour les longues périodes avec conservation des extrema.

Tracer les points observés dans WPF avec infobulles date/rang/LP ; signaler les trous de collecte. Axe ordinal continu des divisions jusqu’à Diamant, puis LP des rangs apex ; seuils GM/Challenger séparés s’ils deviennent disponibles. Afficher variation et pic **sur la période observée**, sans confondre avec un record de saison. Ne pas attribuer une variation à une partie précise si plusieurs matchs séparent deux relevés.

Validation : changement de division, promotion, rétrogradation, saison suivante, non classé, absence de données, observations tardives et intervalles manquants. Budget Riot inchangé si seuls les relevés existants sont enregistrés.

## 3. Compléments accessibles avec nos parties stockées

- Bilans quotidiens : parties, V/D, durée totale, KDA ; regroupement par date locale explicite.
- Joueurs rencontrés : coéquipiers/adversaires récurrents, V/D et navigation vers leur profil, uniquement depuis les détails déjà en cache. Indiquer la couverture si certains détails manquent ; aucune collecte récursive.
- Recherche par champion et filtres de file rapides, conservant la définition de l’échantillon affiché.
- Cartes de parties plus riches : sorts, runes, participants réutilisant les visuels/détails déjà disponibles. Chargement et virtualisation à conserver.

## 4. Données et modèles supplémentaires

Historique de rang par saison et records : construire au fil des observations, ou étudier une source officiellement autorisée avant tout import. Un ancien historique non observé reste indisponible.

Scores de performance et comparaison au rang : définir des métriques explicables et un jeu de référence par patch/rôle/champion, puis calculer côté serveur. Ne pas reproduire ni présenter comme équivalents les scores propriétaires AI-Score/Fate de DeepLoL. Mesurer biais, taille d’échantillon et coût de collecte avant de décider d’un modèle prédictif.
