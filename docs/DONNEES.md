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

Le profil affiche dix positions d’historique à la fois. Le défilement ou **Voir plus** demande automatiquement les positions suivantes, y compris lorsque leurs détails ne sont pas encore en base. Les filtres Solo/Flex/ARAM et autres files sont envoyés au serveur ; Classé utilise le type Riot `ranked` et retient Solo/Flex. L’anticipation prépare les pages suivantes sans collecter toute une saison à chaque ouverture.

La session partagée conserve une borne temporelle et un index durable. Les détails déjà présents sont réutilisés. Un détail indisponible consomme sa position sans devenir une défaite ; une page peut donc comporter moins de dix cartes. En cas de quota ou d’indisponibilité, le client reste lisible et reprend automatiquement. La fin signifie que la source a renvoyé la dernière page accessible pour cette session, pas que la carrière entière du compte est garantie.

Il n’y a plus de plafond applicatif à 10 000 identifiants dans le collecteur ni de quota de trois recherches pour la navigation. Les limites de disponibilité/rétention et les quotas de Riot s’appliquent toujours. Les tailles de page sont des unités de traitement, pas un plafond d’historique.

`RIFT_HISTORY_BACKFILL=false` reste le défaut : il désactive le parcours anticipé de toute la période depuis `RIFT_HISTORY_FROM`, **pas** la collecte demandée par le défilement. Avec `true`, ce parcours de fond utilise aussi des pages de vingt identifiants et des checkpoints après chaque détail. La collecte récente automatique reste distincte, toutes les quinze minutes pour les profils actifs.

Un match enrichit ses participants mais ne déclenche pas une exploration récursive de leurs historiques. Aucun préchargement massif EUW n’est actif.

## Lecture de l’interface

- **Bilan** : les vingt premières positions du filtre, hors détails indisponibles, remakes et durées invalides. Il se complète progressivement mais ne grossit pas avec les pages ouvertes.
- **Champions récents** : top trois sur le même échantillon de vingt positions du filtre ; les rôles utilisent aussi cet échantillon.
- **Rôles** : parties à rôles classiques uniquement ; pas de rôle inventé en ARAM/Arena/URF ni à partir du champion. Les barres victoires/défaites partagent une échelle absolue.
- **Champions · saison** : agrégats déjà présents en base, filtres Classé/Solo/Flex, période depuis le 8 janvier 2026 dans le client actuel. Le calendrier doit être rendu explicite/configurable avant la saison suivante ; changer seulement `RIFT_HISTORY_FROM` ne change pas cette borne d’affichage.
- **Rangs** : compteurs Riot actuels, indépendants de l’échantillon des matchs. Les rangs des participants affichés dans les détails sont actuels et datés, pas ceux observés à la date du match.
- **Route des divisions** : position dans les paliers, pas historique de promotions. Aucun historique LP passé n’est reconstruit.

KDA = somme(kills + assists) / max(1, somme(morts)). Les moyennes K/D/A restent distinctes de ce ratio. CS = sbires + monstres. Les métriques par minute utilisent les totaux et la durée cumulée ; les indicateurs non applicables restent absents. Les slots d’objets vides sont invisibles. Les descriptions d’objets/runes viennent du catalogue actuel, sans garantie de correspondre au patch d’une ancienne partie.

## Travail et limites

| Limite actuelle | Valeur / comportement |
|---|---|
| Navigation | Aucun budget par nombre de pages/recherches ; identité/session connue réutilisée |
| Admission de nouveau travail | 1 000 identités non résolues actives et 1 000 sessions actives avec travail restant ; au-delà, reprise côté client avec données en cache |
| Actualisations acceptées | 5 par 10 minutes pour l’opérateur privé |
| Délai par profil | 5 minutes, fixé dans le code TypeScript actuel |
| Intérêt d’un profil consulté | Prolongé de 30 minutes |
| Collecte récente terminée | Prochaine échéance automatique à 15 minutes, si le profil reste actif |
| Limiteur Riot | Réservations persistantes par hôte : 18/s et 90/2 min, fenêtres applicatives et par méthode distinctes, avec Retry-After |
| API privée | 64 requêtes authentifiées simultanées par processus ; surcharge temporaire avec Retry-After |

`RIFT_REFRESH_COOLDOWN_SECONDS` existe dans le Compose historique mais n’est pas lu par le Store TypeScript. Ne pas le présenter comme un réglage opérationnel du nouveau serveur.

Les erreurs 401/403 Riot sont distinguées des quotas et entraînent une pause d’accès refusé. Le healthcheck de l’API n’effectue pas de test authentifié Riot. Les délais, cache et déduplication réduisent les appels ; augmenter la concurrence ne réduit pas leur nombre.

Sources officielles : [portail Riot et quotas](https://developer.riotgames.com/docs/portal), [LoL / API et assets](https://developer.riotgames.com/docs/lol), [conditions API](https://developer.riotgames.com/terms). Les droits d’accès au code d’un connecteur et les droits de redistribution de ses données sont distincts.

## Passage à une utilisation publique

Ce mécanisme mutualise les données et borne la demande, mais ne prouve pas une capacité de milliers d’utilisateurs. La clé partagée actuelle ne permet pas l’équité par compte. Avant le public : authentification individuelle, budgets par compte, métriques de file/fraîcheur/cache/latence, tests de charge et clé Riot de production adaptée. Les sessions inactives cessent d’être collectées après trente minutes ; leurs index sont conservés pour la reprise. Une politique de rétention/compaction de ces index reste à définir avant une exploitation prolongée à grande échelle.

Les bilans 30 jours/période personnalisée, le remplissage automatique complet des statistiques de saison, les listes de profils suivis et le signal de fin de partie ne sont pas encore intégrés. Les agrégats de saison restent explicitement partiels.

Correctif du limiteur : les fenêtres applicatives et celles de chaque méthode sont enregistrées séparément dans `riot_scoped_windows`. Une réservation est atomique pour les deux budgets ; un refus local ne consomme aucune réservation. Les anciennes fenêtres mélangées ne sont plus utilisées ; l’historique des appels et les pauses persistantes restent conservés. Les nouvelles réponses Riot mettent à jour les limites observées, sans conserver systématiquement le minimum historique. `riot_quota_wait` signifie une attente décidée localement ; `riot_rate_limited` correspond à une réponse HTTP 429. Le client conserve le motif et la date de reprise à l’écran.
