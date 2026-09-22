# Rang mondial et serveur — recherche du 19 septembre 2026

## Nouvelle piste vérifiée — OP.GG, 21 septembre 2026

**Mise à jour v28 :** rang serveur et statistiques saisonnières désormais intégrés automatiquement au profil et à l’overlay, avec cache daté d’une heure. Voir [OPGG.md](OPGG.md). Les réserves sur le mondial, le Flex distinct, le périmètre et la fraîcheur restent valables. La recherche ci-dessous retrace le test initial.

Le serveur officiel [opgg-mcp](https://github.com/opgginc/opgg-mcp) répond à `https://mcp-api.op.gg/mcp`. Test direct sans clé : `tools/list`, puis `tools/call` sur `lol_get_summoner_profile` pour KalAram#ARAM / EUW. Le catalogue expose `data.summoner.ladder_rank.{rank,total}`, décrit comme classement régional.

Résultat observé : rang **112 379 / 3 426 740**, soit environ **Top 3,28 %** calculé sur cette population. Le profil retourné était daté du **19 septembre 2026 à 04:13:42 +09:00**, avec Diamond III 44 LP ; ce résultat est un instantané OP.GG, pas le classement live de la capture du 21 septembre (Diamond III 4 LP). `region` était null dans la réponse malgré EUW dans la requête. Ne pas présenter ces valeurs comme actuelles sans date.

Conclusion : piste techniquement confirmée pour un rang régional OP.GG. Pas de champ mondial identifié. `ladder_rank` est unique au niveau du profil, pas décliné par file ; l’affectation précise Solo/Duo et la couverture Flex restent à confirmer. Le leaderboard de champions concerne les meilleurs joueurs Master+, pas le classement global de tous les comptes.

Le résultat MCP du test contient du texte compact avec déclaration de classes et valeurs imbriquées, pas un objet JSON métier dans `structuredContent`. Une intégration C# nécessite donc de valider le contrat de réponse et son parsing, les champs absents, la fraîcheur, les quotas et les conditions d’utilisation du service. La licence MIT du dépôt porte sur le code ; elle ne constitue pas à elle seule un contrat d’accès durable à toutes les données hébergées. Rien n’a été branché automatiquement dans les profils à ce stade, aucune saisie manuelle.

Intégration envisageable : enrichissement indépendant et annulable, cache daté, requête ciblée aux champs nécessaires, attribution « Rang serveur OP.GG », absence explicite pour mondial/Flex non confirmé. Aucun besoin d’exécuter un modèle IA dans Rift pour appeler un serveur MCP.

Autres éléments utiles : builds, runes, counters et synergies via les outils du même serveur ; historique de LP et statistiques de champions dans le profil. Le dépôt [php-riotapi-request](https://github.com/opgginc/php-riotapi-request) est un client Riot PHP à requêtes asynchrones/concurrence réglable, pas un accès à l’index OP.GG ; son README cite encore des endpoints anciens et les rate limits en TODO. Peu d’intérêt à importer ce runtime dans WPF. [laravel-mcp-server](https://github.com/opgginc/laravel-mcp-server) sert à héberger des outils MCP en PHP, pas à fournir un classement LoL.

Les conclusions ci-dessous décrivent la recherche précédente ; OP.GG constitue désormais une piste régionale vérifiée, sans solution mondiale complète validée.

## Besoin

Afficher automatiquement la position mondiale, la position sur le serveur et le Top %, pour Solo/Duo et Flex. Pas de saisie manuelle. Aucune estimation à partir du seul palier/LP présentée comme un rang exact.

## Résultat vérifié

La [FAQ Porofessor](https://porofessor.gg/fr/faq) indique que le rang combine les classements Riot de toutes les régions. Son créateur est également celui de LeagueOfGraphs. Aucune API publique officielle de LeagueOfGraphs n'a été identifiée lors de cette recherche. Une tentative HTTP directe sur la fiche publique a reçu 403 : cette source n'est donc pas exploitable de manière fiable par le client actuel. Aucun contournement n'est implémenté.

L'[API Riot](https://developer.riotgames.com/apis#league-v4) fournit les entrées classées, mais le client actuel ne dispose pas d'un index mondial permettant de déduire la position d'un joueur. Son rang dans une petite ligue ne correspond pas à son rang serveur. Additionner quelques distributions arrondies ne produit pas non plus un classement exact.

## Voie automatique retenue pour une future intégration

Un service d'indexation distinct du client WPF, alimenté par les classements Riot, ou un fournisseur qui autorise l'accès à son index. Prérequis : accès aux données, quotas adéquats et hébergement à déterminer. Rien n'a été souscrit ni déployé.

Le service devra couvrir chaque serveur et les deux files, stocker des instantanés datés, définir l'ordre des paliers/divisions/LP et les ex æquo, et ne publier le rang mondial que lorsque toutes les régions nécessaires sont couvertes. Le Top % requiert aussi la population classée de la même file et du même périmètre. Une collecte partielle ne doit jamais être affichée comme un rang mondial complet.

Le client devra faire une seule requête asynchrone par profil à ce service, avec annulation, délai limité et cache, sans ralentir l'affichage des matchs. Afficher la date de l'instantané ; conserver le dernier résultat daté hors ligne. La clé du service Riot reste côté serveur.

## État livré

Le lien vers la fiche LeagueOfGraphs reste disponible. L'affichage automatique mondial/serveur n'est pas livré faute de source automatisable validée. Les contrôles synthétiques ne valident pas une intégration réelle à LeagueOfGraphs.
