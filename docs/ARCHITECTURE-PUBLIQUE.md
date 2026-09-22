# Proposition : client léger et serveur — 21 septembre 2026

Statut : première tranche fonctionnelle en v31, avec backend Docker et mode serveur du client natif. Voir [server/README.md](../server/README.md) pour l’implémentation exacte, les tests et les limites. Les sections suivantes décrivent la cible complète ; ce n’est pas une autorisation de distribution publique. Le mode local v30 est conservé dans v31 pendant la transition.

Implémenté : collecte partagée, agrégats PostgreSQL, API privée, worker et notifications SignalR. Les participants d’un match enrichissent leur profil sans devenir automatiquement des comptes suivis. Écart volontaire au plan initial : SQL/Npgsql directement, schéma initial versionné, et ordonnanceur Quartz en mémoire au-dessus de notre file durable PostgreSQL ; EF Core, JobStore Quartz persistant et OpenTelemetry ne sont pas intégrés à cette tranche.

## Choix recommandé

Conserver WPF/.NET 10 pour Windows ; créer une API ASP.NET Core, un worker .NET et une base PostgreSQL. Garder un seul dépôt et des modules séparés, avec deux processus serveur déployables indépendamment (API et worker). Commencer avec un seul worker et un ordonnanceur persistant Quartz.NET. Ajouter un cache distribué ou une file dédiée uniquement si les mesures le justifient.

Le calcul de quelques centaines de matchs n’est pas nécessairement le principal coût actuel. L’architecture centralisée réduit surtout les téléchargements répétés, le stockage, le parsing et les travaux en arrière-plan sur chaque PC. Le rendu, les images, les allocations UI et la composition de la fenêtre transparente restent locaux et demandent des mesures séparées.

## Droits et sources

Trois droits distincts doivent être vérifiés : utilisation du code, accès au service, conservation/redistribution des données et assets. Un endpoint public et une licence open source du connecteur ne valident pas l’ensemble.

| Source | Constat vérifié | Décision proposée pour le public |
|---|---|---|
| Riot API | Enregistrement/audit du produit requis ; clé de production pour un produit public, aucune clé dans un binaire distribué | Source principale, sous réserve d’approbation du produit et respect des conditions |
| Data Dragon / illustrations Riot | Assets prévus par les politiques Riot ; droits sous-jacents et mentions Riot demeurent applicables | Source d’assets privilégiée ; inventorier la provenance des fichiers déjà livrés |
| OP.GG MCP | Code MIT ; conditions du service restrictives ; FAQ et conditions générales présentent des indications contradictoires | Intégration publique non validée ; obtenir un accord écrit couvrant exactement notre usage ou l’exclure de la version publique |
| CommunityDragon / Meraki | Code et distribution de données statiques ne suppriment pas les droits Riot sur les assets | Vérifier fichier/version/licence/droits de redistribution ; ne pas déclarer tous les assets libres par extension |
| League of Graphs, LoLalytics et autres sites | Aucun accord de réutilisation propre à Rift Companion établi | Aucune dépendance de production sans conditions ou contrat appropriés |

Références Riot : [politiques générales](https://developer.riotgames.com/policies/general), [clés de production](https://developer.riotgames.com/docs/portal), [conditions API](https://developer.riotgames.com/terms), [Data Dragon et APIs locales](https://developer.riotgames.com/docs/lol). Le projet et ses fonctionnalités d’overlay/LCU doivent être déclarés ; les identités masquées ne doivent pas être révélées. Prévoir le texte de non-affiliation prescrit et l’examen du modèle de monétisation avant diffusion.

OP.GG : la [licence MIT](https://raw.githubusercontent.com/opgginc/opgg-mcp/main/LICENSE) porte sur le logiciel. Les [conditions générales](https://op.gg/lol/policies/agreement), datées du 14 septembre 2026 lors de cette lecture, restreignent notamment la réutilisation commerciale sans consentement écrit et le scraping. La [FAQ](https://help.op.gg/hc/en-us/articles/31091405109401-Can-I-use-OP-GG-data), plus ancienne, est plus permissive sur le crawling tout en demandant une attribution. Aucun droit spécifique suffisant au MCP pour notre produit public n’a été confirmé. Demander confirmation de la redistribution, du cache, des agrégats dérivés, de la monétisation, des quotas et de l’attribution. Aucun contact n’a été envoyé dans cette tâche.

Le retrait du nom du fournisseur de l’interface en v30 n’est donc pas une décision juridique acquise. Une attribution devra être réintroduite si les conditions applicables l’exigent, à l’emplacement accepté par ces conditions.

[CommunityDragon](https://github.com/CommunityDragon/Docs#legal) distingue son travail des assets Riot. Le [dépôt Meraki](https://github.com/meraki-analytics/lolstaticdata) décrit une distribution de données statiques, pas un droit illimité sur toutes leurs composantes. Avant release : registre des sources, versions et obligations, inventaire des dépendances transitives, notices légales et SBOM. Cet examen ne constitue pas un audit exhaustif des assets/dépendances déjà présents.

## Répartition des responsabilités

### Client

- Interface WPF, overlay, raccourcis, placement des fenêtres et accessibilité.
- Détection locale de LoL et lecture des seuls événements utiles via les interfaces locales appropriées. Les mots de passe LCU/lockfile restent sur le PC.
- Client HTTPS vers notre API et connexion SignalR uniquement lorsque utile.
- Petit cache borné des derniers modèles d’affichage et des images ; affichage immédiat des données connues, avec date.
- Décodage des images hors UI, taille adaptée au rendu et réutilisation des bitmaps ; diagnostics détaillés activés à la demande.
- Plus de clé Riot demandée aux utilisateurs publics, plus d’import historique ni de recalcul de saison local.

Le client ne peut pas être littéralement limité au dessin : le serveur n’a pas accès aux interfaces LoL locales, et les raccourcis/gestion de fenêtres restent nécessairement sur le PC. Riot documente aussi que le LCU n’est pas une interface tierce officiellement supportée : isoler cet adaptateur et prévoir un mode dégradé.

### Serveur

- Identités et plateformes, accès Riot avec secret conservé côté serveur.
- Acquisition de listes et détails de matchs, déduplication région + ID, rangs actuels datés.
- Agrégats par joueur, période/saison, file, champion et rôle ; compteurs additionnels, moyennes calculées à partir des sommes correctes.
- Calendrier explicite des saisons, modèle des remakes, files sans rôles et indicateurs non applicables.
- File de tâches durable, priorités, déduplication des travaux, limites de concurrence et budget partagé de quotas.
- Provenance interne, couverture, horodatages, versions de formule ; recalcul contrôlé si les règles changent.
- Modèles d’affichage compacts, pagination des détails, notifications de version modifiée.

Un match téléchargé une fois peut servir aux profils concernés, dans les limites de conservation retenues. Une contrainte unique sur le match et un registre de contributions évitent de compter deux fois un résultat après reprise d’un job. Ne pas accumuler une collection mondiale sans besoin produit défini.

## Mise à jour automatique

1. Le client demande un profil ; l’API renvoie immédiatement le dernier état enregistré.
2. Une tâche est planifiée si la fraîcheur est insuffisante, avec regroupement des demandes concurrentes.
3. Le worker récupère les nouveaux matchs et met à jour les agrégats dans une transaction.
4. Une notification SignalR indique la nouvelle version ; le client récupère seulement ce qui a changé.
5. Après reconnexion, un GET réconcilie la version : la notification n’est pas l’unique garantie de mise à jour.

Un événement local de fin de partie peut accélérer la vérification serveur. Il reste un indice non fiable fourni par le client : le serveur confirme les données auprès de Riot. La collecte normale Match-v5 repose sur une planification ; ne pas supposer l’existence d’un webhook universel de fin de partie.

Priorités proposées : fin de partie et profils actuellement consultés, joueurs actifs, puis rattrapage historique et profils dormants. Après une fin de partie, attendre la disponibilité des résultats et réessayer avec espacement croissant et borne. Pour les comptes inactifs, réduire fortement la fréquence. Les utilisateurs n’appuient plus sur Synchroniser, mais un premier profil inconnu peut encore nécessiter un chargement progressif. Afficher « Mise à jour en cours » et une date, sans promesse de temps réel absolu.

Les quotas restent partagés par la clé et les routes/méthodes concernées : respecter les en-têtes Riot, les 429 et Retry-After. Une clé de production n’est pas illimitée. Toute extension à plusieurs workers exige un coordinateur commun des quotas ; ne pas multiplier artificiellement les clés.

## Technologies

| Composant | Choix initial | Motif |
|---|---|---|
| Client Windows | WPF + .NET 10 | Réutilise l’application et l’intégration native existantes ; mesurer avant une réécriture |
| API | ASP.NET Core, REST/JSON, contrats versionnés et OpenAPI | Même langage que les calculs actuels ; modèles légers, cache HTTP/ETag |
| Traitements | .NET Worker Service | Les collectes lentes ne bloquent pas les réponses de l’API |
| Ordonnancement | Quartz.NET + stockage PostgreSQL persistant | Reprises et planification ; licence Apache-2.0 du projet |
| Données | PostgreSQL, Npgsql ; EF Core pour le modèle/migrations | Relations, index, unicité et transactions ; SQL ciblé pour les agrégats si nécessaire |
| Notifications | SignalR | Intégration .NET et poussée des versions de profils ; authentification/autorisation des abonnements |
| Images | Assets validés versionnés, cache local, éventuellement stockage objet/CDN | Pas de reconstruction du cache à chaque profil ; hébergement selon les droits obtenus |
| Exploitation | Conteneurs Linux API/worker, PostgreSQL sauvegardé ; OpenTelemetry | Déploiement simple et diagnostic CPU, RAM, latence, quotas, retard des jobs |

Références : [SignalR et workers](https://learn.microsoft.com/en-us/aspnet/core/signalr/background-services?view=aspnetcore-10.0), [Quartz, stockage](https://www.quartz-scheduler.net/documentation/quartz-4.x/tutorial/job-stores.html), [licence Quartz](https://github.com/quartznet/quartznet/blob/main/license.txt), [licence PostgreSQL](https://www.postgresql.org/about/licence/). Choisir et verrouiller les versions stables compatibles lors de l’implémentation, puis vérifier leurs dépendances exactes.

Pour une première bêta : une application serveur modulaire, API et worker séparés, PostgreSQL, sauvegardes et HTTPS. Pas de besoin démontré de Kubernetes, Kafka, microservices multiples ou cache distribué dès le départ. L’hébergeur et le dimensionnement dépendront du nombre de comptes actifs, des sessions simultanées, de la fraîcheur cible et du budget ; aucun tarif non vérifié n’est avancé.

## Performances et produit public

Mesurer le jeu seul, puis client ouvert, overlay masqué, overlay affiché et détaché, dans une scène comparable. Suivre temps de frame p95/p99, 1 % low FPS, CPU/RAM du client, GPU/composition, octets réseau et décodages d’images. Le serveur n’élimine pas le coût d’une grande surface WPF transparente. Réduire les reconstructions de cartes et animations, regrouper les mises à jour et arrêter les travaux non utiles lorsque l’UI est cachée. Voir les [recommandations WPF sur les images](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-2d-graphics-and-imaging).

Définir des budgets sur une machine de référence, mesurer avant/après, puis bloquer les régressions. Ne pas annoncer un gain de FPS avant mesure. Conserver le mode hors ligne en lecture et un délai réseau borné : un problème serveur ne doit pas empêcher le raccourci de masquer l’overlay.

Pour la diffusion : installateur et mises à jour signés, retour arrière, API compatible avec au moins la version précédente du client, secrets uniquement serveur, contrôle d’accès, limitation des recherches et quotas par session contre les collectes abusives. L’identité déclarée par le PC ne doit pas prouver à elle seule la propriété d’un compte ; envisager Riot Sign On si nécessaire et approuvé.

Les identifiants de joueurs, historiques et télémétrie nécessitent une politique de confidentialité, une base juridique adaptée, des durées de conservation et la gestion des droits applicables. Les données publiquement consultables ne dispensent pas de ces obligations. Prévoir minimisation et purge selon les [principes CNIL](https://www.cnil.fr/fr/comprendre-le-rgpd/les-six-grands-principes-du-rgpd). Ne pas envoyer de secrets LCU ou de diagnostics bruts identifiants au serveur.

## Migration proposée

1. Dossier Riot, registre des sources et décision explicite sur les fournisseurs autorisés pour la distribution.
2. Créer `Rift.Contracts`, `Rift.Server`, `Rift.Worker` et des services de calcul indépendants de WPF. Extraire les tests existants sans changer les formules en même temps.
3. PostgreSQL : matchs/participants minimaux, agrégats, fraîcheur et jobs. Implémenter ingestion idempotente, reprises, quotas et profils pré-calculés.
4. Ajouter un fournisseur de profils HTTP au client. Comparer sur des fixtures les résultats serveur/local ; conserver le chemin local uniquement pendant cette validation.
5. Basculer profil puis overlay vers le serveur, supprimer le formulaire de clé et la synchronisation locale de la distribution publique. Ajouter actualisation automatique, cache et mode dégradé.
6. Bêta limitée, mesures en jeu et en charge, vérification des droits/mentions, sauvegardes testées et déploiements signés avant ouverture large.

Le rang mondial reste un problème de couverture/indexation distinct. Déplacer les calculs au serveur ne crée pas les classements absents des sources ; une indexation mondiale ou un fournisseur autorisé est nécessaire.
