# Actualisation et quotas — v33

## Direction active v34 : échantillon affiché

Le rattrapage de saison est désactivé par défaut (`RIFT_HISTORY_BACKFILL=false`). Une recherche résout l’identité puis collecte seulement la première page de 20 matchs, sans borne de début de saison. Les anciens curseurs de saison sont conservés mais ne sont plus exécutés. Les limites de refresh restent applicables ; les matchs présents ne sont pas retéléchargés. Le rattrapage reste disponible sur activation explicite de l’exploitant.

En mode serveur, KDA, victoires, rôles, champions et autres métriques utilisent les lignes chargées, sélectionnées par le filtre d’historique puis le filtre de chaque panneau. Remakes et durées nulles exclus. KDA = somme(kills + assists) / max(1, somme(morts)), pas moyenne des KDA individuels. Ces petits calculs utilisent les données déjà en mémoire, sans requête Riot ni agrégats de saison distants.

« Voir plus » lit uniquement les pages déjà stockées et agrandit l’échantillon. Un nouveau profil peut donc n’avoir que 20 parties disponibles. Les compteurs du classement Riot restent distincts. Aucune suppression des parties collectées ni limite de rétention à 30 jours.

Les sections v33 suivantes décrivent le rattrapage désormais optionnel. Le mode local historique reste un mode de transition ; cette direction s’applique au mode client/serveur actif.

## Comportement livré

- La consultation d’un profil connu prolonge l’intérêt de 30 minutes, sans avancer son échéance Riot. Filtres et pagination lisent PostgreSQL.
- Une nouvelle identité explicite crée un rattrapage de saison. Le budget privé est de 3 nouvelles recherches par période glissante de 10 minutes. Une recherche connue reste accessible une fois ce budget épuisé.
- `POST /v1/players/{platform}/{puuid}/refresh` demande une actualisation récente. Le serveur conserve un seul job récent par plateforme/PUUID, distinct du curseur de saison. Les clics concurrents rejoignent le même travail.
- Délai partagé par profil de 300 secondes par défaut, configurable avec `RIFT_REFRESH_COOLDOWN_SECONDS` (120 à 3600). Une collecte automatique récente applique aussi ce délai. Aucune requête client ne choisit la priorité, le délai ou la période collectée.
- Budget de 5 actualisations acceptées par opérateur sur 10 minutes, tous profils confondus. Contrôle et enregistrement atomiques en PostgreSQL, conservés après redémarrage. L’API est encore privée : l’acteur est fixé côté serveur à `private`, pas fourni par le client. L’authentification publique devra fournir cet acteur à partir d’une session vérifiée.
- Une demande déjà en cours, en délai de reprise ou en cooldown ne crée aucun travail supplémentaire. La réponse indique `status`, `serverTime`, `lastUpdatedAt`, `nextAllowedAt` et `accepted` ; la date d’éligibilité ne promet pas la fin de collecte.
- WPF conserve le profil affiché et montre un compte à rebours basé sur l’heure serveur. L’attente et les erreurs ne bloquent pas la consultation.

## Collecte

Les jobs `recent` et `history` ont chacun leur curseur et leur borne temporelle stable. Les vérifications récentes reprennent avec un chevauchement de 48 heures ; après une inactivité, elles reprennent depuis leur dernière borne, pas seulement depuis aujourd’hui moins 48 heures. Les matchs déjà enregistrés ne sont pas retéléchargés.

Le parcours de saison terminé ne redémarre plus périodiquement. Les vérifications récentes restent espacées de 15 minutes tant qu’un profil est actif. Les abonnements de la page visible prolongent l’intérêt ; après expiration, les tâches s’arrêtent jusqu’à une nouvelle consultation. Les participants d’une partie ne déclenchent pas de parcours de saison.

Trois tours de sélection sur quatre favorisent les nouvelles identités/les données récentes. Le quatrième favorise le rattrapage de saison. C’est une répartition des tours, pas une garantie de pourcentage de requêtes ou de latence. Quotas Riot, reprises et disponibilité continuent de s’appliquer.

Migration PostgreSQL v2 additive : conserve matchs, agrégats et curseurs existants ; crée les jobs récents pour les profils déjà suivis. Ne pas relancer un ancien backend v31/v32 sur ce schéma. Un retour arrière nécessite le code compatible v2 ou la restauration de la sauvegarde préalable.

## DeepLoL : observations et limites

Dans les captures fournies, `203W 207L` représente 410 parties classées, et `Last 30d` qualifie le graphique de LP. L’historique montre une partie vieille de 29 jours avec un bouton `View 20 more games` : cela ne démontre pas une limite de rétention à 30 jours.

Recherche sur le [site officiel DeepLoL](https://www.deeplol.gg/) et ses pages indexées : aucune règle publique de rétention/collecte à 30 jours trouvée. Ne pas assimiler cette absence de documentation à une preuve de leur fonctionnement interne.

Notre code Riot récupère déjà wins/losses avec les entrées League-V4, indépendamment des détails Match-V5. Nous pouvons donc afficher le bilan classé avant la fin de l’import, sans en déduire que KDA, rôles et champions ont une couverture complète. Les statistiques détaillées restent explicitement partielles jusqu’à la fin du parcours accessible. Aucune réduction à 30 jours n’est appliquée à la demande de saison de l’utilisateur.

## Étapes suivantes du plan

- Remplacer l’acteur privé par des comptes authentifiés et ajouter une protection IP complémentaire avant toute ouverture publique.
- Séparer finement les quotas application/méthode/service, et adapter les plafonds à la clé Production. Le limiteur conservateur actuel reste en place.
- Espacement progressif des vérifications sans nouveau match, traitement borné des signaux de fin de partie et budget spécifique aux consultations de l’overlay.
- Tableau de bord serveur : taux de cache, travaux fusionnés, refus, attente et coût des nouveaux profils.

Sources : [Riot — quotas](https://developer.riotgames.com/docs/portal#rate-limiting), [OP.GG — retard d’historique](https://help.op.gg/hc/en-us/articles/31088795082393-My-stats-aren-t-updating), [OP.GG — classement horaire](https://help.op.gg/hc/en-us/articles/31089172140441-My-summoner-ranking-isn-t-updating).
