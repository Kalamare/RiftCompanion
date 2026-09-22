# Diagnostic en direct

Depuis v18, le bouton **Diagnostic en direct** ouvre une fenêtre dédiée depuis le profil ou le détail d’une partie. Elle reste ouverte pendant les chargements. Si une fiche modale est ouverte, utiliser son bouton pour rattacher le diagnostic à cette fiche. « Suspendre l’affichage » fige les valeurs pour les lire ; les compteurs continuent de collecter.

## Métriques

- PID, runtime .NET et nombre de processeurs logiques.
- CPU du processus rapporté à la capacité totale du PC ; mémoire résidente, mémoire privée et pic de mémoire résidente.
- Mémoire .NET, débit d’allocations, taille et fragmentation du tas au dernier GC, compteurs des générations 0/1/2. Aucun GC forcé.
- Threads Windows avec état observé et temps CPU cumulé ; nombre de handles, threads du ThreadPool et travaux en attente.
- Retard du relevé sur le dispatcher : retard par rapport à l’intervalle d’une seconde, pas temps de rendu d’une frame.
- Requêtes terminées et rythme par seconde, opérations en cours, cumuls par catégorie, erreurs HTTP/réseau, annulations ou délais.
- Accès au cache mémoire des images, décodages et résultats de recherche dans le cache SQLite des parties.

## Activité instrumentée

`RuntimeDiagnostics` dans Rift.Core contient des scopes synchronisés et un journal circulaire de 200 opérations terminées. Les cumuls persistent en mémoire pendant la session. `DiagnosticHttpHandler` couvre les clients Riot, LCU, Data Dragon et le catalogue des modes. Les étiquettes de requête sont fixes ; aucune URL, clé, identité, réponse ou exception brute n’est journalisée.

Les scopes internes couvrent le chargement du profil/pagination, la préparation des illustrations, le détail de partie, les rangs des participants, la construction du profil et de ses filtres, les décodages bitmap, les attentes de quota Riot et les écritures de détails SQLite. Le journal montre aussi les opérations trop brèves pour apparaître lors du relevé d’une seconde.

Ces opérations ne sont pas des processus indépendants : elles partagent les threads et les allocations du processus WPF. Leur durée est une durée murale, incluant les attentes. Les temps HTTP vont jusqu’aux en-têtes ; les scopes de chargement englobent la lecture et le traitement des réponses. « Terminé » indique la fin d’un scope ; les statuts HTTP, erreurs et annulations sont détaillés lorsqu’ils sont instrumentés.

## Coût et limites

Les relevés processus s’exécutent hors du dispatcher, sans chevauchement, uniquement pendant l’ouverture de la fenêtre ; la fermeture arrête le timer. L’affichage est actualisé toutes les secondes, sauf pendant la suspension ou la minimisation. L’instrumentation légère et bornée des opérations reste active pour conserver l’activité récente même avant l’ouverture du diagnostic.

La RAM et le CPU comprennent le coût du diagnostic lui-même. Ces mesures ne couvrent pas LoL, les autres instances éventuelles de Rift Companion, le GPU, les FPS ni une ventilation exacte de la RAM/CPU par tâche. Pour mesurer l’impact sur une partie, comparer aussi avec le diagnostic fermé.


## Analyse automatique (v19)

Le nouvel onglet fonctionne localement lorsque le diagnostic est visible et que l’affichage n’est pas suspendu. Un maximum de 120 relevés / 120 secondes est conservé ; un intervalle sans observation de plus de cinq secondes remet la fenêtre d’observation à zéro. L’analyse ne collecte ni ne transmet de nouvelles données réseau.

| Signal | Condition indicative |
| --- | --- |
| CPU soutenu | Tous les relevés des 10 dernières secondes ≥ 15 % de la capacité totale du PC ; au moins 8 relevés sur 8 secondes |
| Allocations soutenues | Même durée minimale, ≥ 50 Mo/s |
| Retard UI répété | Au moins 3 relevés retardés de 250 ms dans les 10 dernières secondes |
| Hausse mémoire | Environ une minute d’observation ; comparaison des moyennes des 10 premiers/derniers relevés : +100 Mo et +20 % de mémoire privée |
| Quota | HTTP 429 dans les 30 dernières secondes du journal |
| Erreurs réseau | Au moins 3 erreurs réseau/HTTP 4xx ou 5xx dans les 30 dernières secondes |
| Chargement prolongé | Scope Chargements encore actif après 30 secondes |
| Décodages lents | Au moins 3 décodages Images ≥ 100 ms dans les 30 dernières secondes |

Chaque signal décrit les observations et une piste de vérification. Les seuils sont des heuristiques explicites, pas des garanties universelles. Un chargement initial, le remplissage d’un cache ou la charge du PC peuvent expliquer certains signaux. Une hausse mémoire n’est pas une preuve de fuite. Le journal étant limité à 200 opérations, une activité très intense peut réduire la profondeur observée des erreurs/décodages. L’absence de signal signifie uniquement qu’aucune règle ne se déclenche sur les observations disponibles.


## Overlay (v21)

Les requêtes locales de la Live Client Data API apparaissent dans la catégorie `Live` et le chargement des profils participants dans `Overlay`. Ce dernier inclut les attentes réseau et de quota, pas seulement du CPU. Masquer/minimiser l’overlay arrête ses lectures et annule ses chargements. Le diagnostic ne mesure toujours pas les FPS du jeu ; voir [OVERLAY.md](OVERLAY.md) pour la comparaison.
