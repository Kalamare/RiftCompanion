# Données, statistiques et quotas

Référence : implémentation TypeScript au 25 octobre 2026. Les valeurs ci-dessous décrivent le code actuel, pas les garanties d’un service tiers.

## Sources actives

| Donnée | Source |
|---|---|
| Riot ID / PUUID | Riot Account-v1 |
| Niveau / icône de profil | Summoner-v4 |
| Classement Solo/Flex actuel | League-v4 |
| Matchs et participants | Match-v5 |
| État local de draft / partie | LCU et Live Client Data API |
| Illustrations et textes statiques | Catalogues/PNG intégrés et CDN public Data Dragon |

OP.GG n’est pas utilisé par le client Electron ni le worker TypeScript. Les anciennes recherches sur OP.GG et le classement mondial sont [archivées](ARCHIVES.md). Les notices de provenance restent dans `native/Rift.Desktop/Assets` et `native/Rift.Core/Data`.

## Historique et couverture

Le profil affiche dix parties par page. La collecte récente demande dix identifiants Riot ; les détails déjà présents sont réutilisés. Un ancien historique en base ou les contributions d’un joueur à des matchs déjà collectés peuvent fournir davantage de lignes.

**Voir plus** lit PostgreSQL, sans déclencher une collecte Riot plus ancienne. Le curseur et la borne temporelle sont conservés pendant la pagination ; une mise à jour automatique conserve les pages ouvertes. En fin de données disponibles, le client indique que l’historique collecté peut être partiel.

`RIFT_HISTORY_BACKFILL=false` est le défaut. Lorsqu’il est explicitement activé, le rattrapage parcourt l’historique depuis `RIFT_HISTORY_FROM` par pages de 20 identifiants. Le curseur est durable et limité à 10 000 identifiants par travail. Un détail absent/inexploitable ne devient pas une défaite. Une couverture terminée décrit l’historique accessible, pas nécessairement toute la carrière du compte.

Un match enrichit ses participants mais ne déclenche pas une exploration récursive de leurs historiques. Aucun préchargement massif EUW n’est actif.

## Lecture de l’interface

- **Bilan** : échantillon chargé et filtré, hors remakes et durées invalides.
- **Champions récents** : top trois sur les vingt dernières parties chargées au maximum, indépendant du filtre de la liste.
- **Rôles** : parties à rôles classiques uniquement ; pas de rôle inventé en ARAM/Arena/URF ni à partir du champion. Les barres victoires/défaites partagent une échelle absolue.
- **Champions · saison** : agrégats déjà présents en base, filtres Classé/Solo/Flex, période depuis le 8 janvier 2026 dans le client actuel. Le calendrier doit être rendu explicite/configurable avant la saison suivante ; changer seulement `RIFT_HISTORY_FROM` ne change pas cette borne d’affichage.
- **Rangs** : compteurs Riot actuels, indépendants de l’échantillon des matchs. Les rangs des participants affichés dans les détails sont actuels et datés, pas ceux observés à la date du match.
- **Route des divisions** : position dans les paliers, pas historique de promotions. Aucun historique LP passé n’est reconstruit.

KDA = somme(kills + assists) / max(1, somme(morts)). Les moyennes K/D/A restent distinctes de ce ratio. CS = sbires + monstres. Les métriques par minute utilisent les totaux et la durée cumulée ; les indicateurs non applicables restent absents. Les slots d’objets vides sont invisibles. Les descriptions d’objets/runes viennent du catalogue actuel, sans garantie de correspondre au patch d’une ancienne partie.

## Travail et limites

| Limite actuelle | Valeur / comportement |
|---|---|
| Nouvelles recherches privées | 3 par fenêtre glissante de 10 minutes ; recherche connue réutilisée |
| Actualisations acceptées | 5 par 10 minutes pour l’opérateur privé |
| Délai par profil | 5 minutes, fixé dans le code TypeScript actuel |
| Intérêt d’un profil consulté | Prolongé de 30 minutes |
| Collecte récente terminée | Prochaine échéance automatique à 15 minutes, si le profil reste actif |
| Limiteur Riot | Réservations persistantes par hôte : 18/s et 90/2 min, avec Retry-After |
| API privée | 180 requêtes/minute par processus, budget global |

`RIFT_REFRESH_COOLDOWN_SECONDS` existe dans le Compose historique mais n’est pas lu par le Store TypeScript. Ne pas le présenter comme un réglage opérationnel du nouveau serveur.

Les erreurs 401/403 Riot sont distinguées des quotas et entraînent une pause d’accès refusé. Le healthcheck de l’API n’effectue pas de test authentifié Riot. Les délais, cache et déduplication réduisent les appels ; augmenter la concurrence ne réduit pas leur nombre.

Sources officielles : [portail Riot et quotas](https://developer.riotgames.com/docs/portal), [LoL / API et assets](https://developer.riotgames.com/docs/lol), [conditions API](https://developer.riotgames.com/terms). Les droits d’accès au code d’un connecteur et les droits de redistribution de ses données sont distincts.
