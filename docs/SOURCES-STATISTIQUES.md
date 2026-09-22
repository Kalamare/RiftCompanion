# Statistiques par mode et sources — v30, 21 septembre 2026

## Fonctionnement livré

Les blocs Bilan, Champions et Rôles possèdent chacun leur filtre indépendant. Tout, Classé (Solo + Flex pour le calcul local), Soloqueue, Flex, ARAM, Arène et files normales sont proposés ; les autres files rencontrées dans l’historique synchronisé sont ajoutées. Le filtre près de l’identité concerne seulement l’historique récent. Les filtres locaux ne déclenchent aucune requête.

Dans **Bilan de saison → Synchroniser les statistiques**, choisir le début de période et lancer **Calculer / actualiser**. La clé Riot enregistrée est utilisée. La date par défaut en 2026 est le 8 janvier à 00:00 UTC, début du jour du patch 26.1 ; ce n’est pas l’heure exacte d’ouverture du classé de chaque serveur. La date est visible et modifiable. Les résultats calculés indiquent « Depuis le … », sans assimiler le numéro de saison interne d’un fournisseur à une année civile.

La synchronisation est indépendante des 20/+10 matchs de la liste récente. Match-v5 fournit les identifiants par pages de 100 avec `startTime`, `endTime` et `start`. La borne de fin reste fixe pendant une collecte et sa reprise. Les détails sont lus un à un, hors du thread UI, avec le même budget réseau conservateur que les autres appels Riot. Les résultats déjà stockés sont réutilisés.

Les résumés sont sauvegardés dans `season-history-v1` sous le dossier de données de l’application, séparés par plateforme, PUUID et début de période. Les noms de fichiers sont hachés ; les données publiques restent en clair, sans clé API. Maximum 50 sauvegardes ; lecture limitée à 20 Mo par fichier. Chaque page terminée est sauvegardée. Une page interrompue est relue, mais ses détails réussis sont réutilisés depuis SQLite. Après une collecte terminée, Actualiser reparcourt les listes avec une nouvelle borne de fin et télécharge seulement les détails inconnus. Une interruption annule la collecte ; il n’y a pas de relance réseau silencieuse à chaque affichage.

L’UI est mise à jour tous les 20 détails et en fin de page. La collecte est limitée à 10 000 identifiants par période ; si cette borne est atteinte, il faut réduire la période. Les portraits sont décodés hors UI et les filtres utilisent les données en mémoire. Aucune collecte saisonnière n’est lancée automatiquement par l’overlay. Changer de profil annule la collecte en cours.

« Historique accessible parcouru » signifie que la pagination de l’API est épuisée, pas que tous les matchs historiques existent encore chez Riot. Une réponse 404 ou un détail inexploitable est comptabilisé comme indisponible, jamais transformé en partie perdue. Les remakes sont exclus des agrégats. Les données partielles restent explicitement signalées.

Avant synchronisation, le bilan externe disponible reste affiché uniquement dans son mode compatible. Il n’est pas réétiqueté Tout/Solo/Flex sans preuve du périmètre. Les mentions de fournisseur ont été retirées des textes et infobulles du profil et de l’overlay ; les noms techniques et cette documentation conservent la provenance.

## Calculs

Chaque calcul s’applique après déduplication des identifiants, filtrage de la période et du mode, et exclusion des remakes/durées invalides. Les listes saisonnières complètes de champions ne sont pas nécessaires si les détails de matchs sont disponibles.

| Indicateur | Calcul utilisé |
|---|---|
| Parties, victoires, défaites | Nombre de matchs retenus, compteurs du booléen `win` |
| Taux de victoire | 100 × victoires / parties |
| K / D / A moyens | Somme de chaque compteur / nombre de parties |
| KDA global | (Somme kills + somme assists) / max(1, somme morts), pas la moyenne des ratios individuels |
| CS moyens | Somme `totalMinionsKilled + neutralMinionsKilled` / parties |
| CS / minute | Somme CS / somme des durées en minutes, pas une moyenne non pondérée |
| Or / minute, dégâts / minute | Somme or ou dégâts aux champions / durée totale |
| Participation aux kills | Moyenne des ratios (kills + assists) / kills de l’équipe, uniquement lorsque le dénominateur est positif |
| Vision | Moyenne `visionScore` |
| Champions | Regroupement par ID champion puis mêmes calculs |
| Rôles | `teamPosition`, repli `individualPosition`, seulement dans les files avec rôles standard ; inconnu conservé, jamais déduit du champion |

Arène n’a pas de rôle standard. Sa participation aux kills est laissée indisponible car le total par équipe standard ne représente pas nécessairement la sous-équipe ; `win` suit la définition renvoyée par Riot, pas une hypothèse de première place. Pour un filtre vide, les moyennes restent « — ». La participation affichée pour Tout exclut les parties sans dénominateur utilisable, notamment Arène.

Les champs de Match-v5 sont décrits dans la [référence Riot](https://developer.riotgames.com/apis#match-v5). Le [portail](https://developer.riotgames.com/docs/portal) documente notamment les quotas personnels de 20 requêtes/seconde et 100/2 minutes ; le budget local reste plus conservateur (18 et 90). La première collecte de plusieurs centaines de détails peut donc durer plusieurs minutes. Le calendrier du début 2026 est décrit dans les [notes 26.1](https://www.leagueoflegends.com/en-sg/news/game-updates/patch-26-1-notes/).

## Sources intéressantes

| Source / accès | Données utiles | Place dans Rift Companion / limites |
|---|---|---|
| [Riot Games API](https://developer.riotgames.com/apis) — REST avec clé | Account, Match-v5, League-v4, maîtrise, challenges, spectator selon disponibilité | Source principale pour reconstituer les statistiques personnelles et les modes. Aucun total saisonnier KDA/rôles préagrégé n’est nécessaire : on le calcule. Quotas, données absentes et rétention doivent rester visibles. |
| [OP.GG MCP officiel](https://github.com/opgginc/opgg-mcp) — Streamable HTTP | Profil, rang régional, pool de champions, analyses de champion, builds, runes, matchups, synergies, méta par lane, classements de champions, esports | Complément déjà utilisé pour profil/rang. Le schéma observé de `lol_list_summoner_matches` limite `limit` à 5–20, sans curseur/page ni période : il ne résout pas la collecte complète d’une saison. `lol_get_summoner_profile` ne fournit pas de filtre de file ; dix champions ont été observés sur le compte testé. Les limites observées ne sont pas une garantie éternelle du service. |
| [Data Dragon](https://developer.riotgames.com/docs/lol#data-dragon) — JSON et images | Champions, objets, sorts, runes, versions et traductions | Déjà pertinent pour illustrations et descriptions. Données statiques, aucun historique personnel. |
| [CommunityDragon](https://github.com/CommunityDragon/Docs) — fichiers RAW/CDN | Assets et données du jeu/client plus détaillés, patchs et PBE | Complète les icônes/runes/objets absents de Data Dragon. Aucun classement personnel. Privilégier les fichiers versionnés, pas les adresses d’images arbitraires fournies par des réponses externes. |
| [Meraki Analytics](https://github.com/meraki-analytics/lolstaticdata) — JSON statique et code ouvert | Données enrichies champions/objets | Utile pour descriptions et fiches techniques. Vérifier le patch disponible avant intégration ; le dépôt propose un CDN et demande de mettre les résultats en cache. Pas une base de profils joueurs. |
| [PandaScore](https://developers.pandascore.co/docs/getting-started) — API avec jeton | Calendriers, équipes et résultats esports ; historiques/données live selon offre | Pertinent pour un futur onglet compétition, pas pour calculer le KDA personnel de Soloqueue. Les endpoints et formules d’accès sont détaillés dans la [référence des offres](https://developers.pandascore.co/docs/plan-reference). |
| [League of Graphs / Porofessor](https://www.leagueofgraphs.com/) — site | Classements, profils, statistiques de champions, jungle paths et analyses de matchs | Référence pour la présentation et les comparaisons. Aucune API publique documentée comparable au MCP OP.GG n’a été identifiée pendant cette recherche ; ne pas transformer un endpoint interne ou une page HTML en contrat d’API. |
| [LoLalytics](https://lolalytics.com/) — site | Méta, builds, matchups et statistiques agrégées | Intéressant pour comparer les recommandations. Aucune API publique documentée n’a été confirmée ici. Un accès partenaire serait à vérifier avant intégration. |

Les bibliothèques PHP/Python qui enveloppent Riot ne créent pas une nouvelle base de données : elles gardent les limites de l’API amont. La licence MIT du serveur MCP OP.GG couvre son code, pas une garantie d’accès ou de disponibilité du service hébergé.

## Suite conseillée

Pour le profil : terminer la première synchronisation réelle, comparer des modes identiques et des dates identiques avec les sites, puis mesurer durée et volume des requêtes dans le diagnostic. Pour le prochain apport OP.GG : analyser les builds/runes et matchups du champion sélectionné, avec patch, lane et taille de l’échantillon visibles. Le rang mondial ne peut pas être déduit du rang serveur ou des LP seuls : il reste indisponible sans index mondial fiable.

## Validation

`native/Build.ps1 -Check` : 172 contrôles métier et contrôles WPF. Tests spécifiques : dates, déduplication, remakes, modes, moyennes pondérées, dénominateurs nuls, pagination à borne de fin fixe, cache isolé, détail 404, reprise et annulation. WPF vérifie trois filtres indépendants, Arène/ARAM sans rôle inventé, disponibilité des agrégats et absence de marque fournisseur. Les réponses réseau de ces tests sont simulées ; ils ne prouvent ni l’exhaustivité de l’historique réel d’un compte ni les FPS en jeu.
