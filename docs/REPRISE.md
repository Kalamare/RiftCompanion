# Reprise cloud — 24 octobre 2026

Le blocage de téléchargement Microsoft de l’ancien environnement est levé dans
la nouvelle instance. Configuration et commandes : [CLOUD-TEST.md](CLOUD-TEST.md).
Tests synthétiques validés ; aucune collecte Riot réelle. Publication autorisée
sur `codex/cloud-backend-handoff`, avec un workflow de compilation et contrôles
Windows. Validation Riot bloquée par HTTP 403 : vérifier/renouveler le secret
avant reprise. Hébergement durable à préciser.

# Reprise — v41, 23 octobre 2026

## Transfert vers l’autre PC

Cette livraison regroupe les changements v35 à v41. Après récupération de `main`, compiler avec `native/Build.ps1 -Check` puis lancer `Lancer-Natif.cmd`. Les journaux de validation v41 confirment les contrôles natifs/WPF, 40 contrôles PostgreSQL et les tests REST/SignalR ; ils n’ont pas été relancés pour cette sauvegarde Git.

Git ne transfère ni les secrets de `server/.secrets`, ni les exécutables, ni le volume PostgreSQL local. Configurer les secrets séparément. Pour conserver l’historique sur un autre backend local, prévoir une sauvegarde/restauration PostgreSQL ; ne pas supprimer le volume réel. Le projet Compose réel précédemment utilisé est `rift-real`, port 5081, avec `RIFT_FIXTURE_MODE=false` et `RIFT_HISTORY_BACKFILL=false`.

Le préchargement EUW reste une proposition non démarrée : population initiale ciblée, file persistante bornée, déduplication des matchs, demandes interactives prioritaires et rattrapage de saison sur quota restant. Ne pas activer une exploration récursive sans plafond. Distinguer joueurs connus et historiques complets. Aucun dataset externe n’a été importé ; compatibilité des identifiants et droits à vérifier avant usage public.

Version active : **native-profile-v41** (`native/Build.ps1 -Check`, puis `Lancer-Natif.cmd`).

## Dix matchs par défaut — v41

- Collecte récente bornée à 10 matchs, pages REST de 10 matchs et affichage initial de 10 matchs ; la réconciliation automatique conserve les pages déjà ouvertes. Le bouton Voir plus lit les matchs déjà stockés.
- Le rattrapage de saison reste désactivé. Sa page historique actuelle reste à 20 ; une future collecte de saison par pages de 100 identifiants réduirait le coût de découverte. Les statistiques de saison restent partielles tant que tous les détails pertinents ne sont pas stockés.
- Exemple théorique sans cache : profil + 10 matchs = 14 appels Riot. Pour 750 matchs de saison incluant ces dix, avec une collecte de saison distincte par pages de 100 : 3 identité + 1 liste récente + 8 listes saison + 750 détails = 762 appels (760 EUROPE, 2 EUW1), hors erreurs/reprises.

## Chargement progressif — v40

- La fiche demandée apparaît immédiatement : identité saisie, rangs en attente, blocs de statistiques et historique en chargement. Aucun « Non classé » ni pourcentage nul n’est déduit d’une réponse absente. Annuler ou échouer retire l’état de chargement actif ; le message reste visible.
- Le collecteur publie successivement le compte Riot, le niveau/portrait et les rangs. Les matchs sont déjà enregistrés et notifiés individuellement. Une réponse partielle conserve les rangs et le niveau précédemment connus.
- Le client attend une identité avec annulation possible, sans abandon automatique après 90 secondes. Pendant une pause de quota, la lecture du statut est espacée à cinq secondes et affiche l’heure de reprise fournie par le serveur. Ces lectures ne consomment pas de quota Riot.
- Un refus 401/403 reste identifié comme `riot_access_denied`, y compris pour les autres nouvelles recherches pendant cette pause. Aucun contournement du délai, aucune augmentation des budgets ou du backfill. Si le refus persiste, vérifier le secret Riot côté serveur et recréer le worker après remplacement ; ne jamais mettre la clé dans le client.
- Diagnostic réel du 24/09 : pause de 15 minutes après refus d’accès, sans appels sur les deux dernières minutes, puis reprise réussie. Danteeehh#94MGL est désormais prêt ; 73 matchs sont présents en base (consultation paginée par 20).
- Cette version modifie le serveur : reconstruire et redéployer API + worker avec le même projet Compose et le même volume. Pas de migration de schéma ni suppression de données.
- Une barre commune Total/Solo/Flex/ARAM et « Plus de modes » remplace les filtres répétés du résumé. Les blocs récents et l’historique suivent ce mode ; pas de sélecteur de rôle ajouté.
- Le top champions affiche uniquement trois champions des 20 dernières parties chargées, avant filtrage du mode et hors remakes. Charger des parties plus anciennes ne remplace pas cette fenêtre récente. Les autres moyennes continuent à porter sur les parties affichées.
- À gauche : Solo et Flex visibles, puis champions de saison avec Classé (Solo + Flex)/Solo/Flex, six lignes et « Voir plus ». Lecture de `/stats` sur la période à partir du 08/01/2026 et uniquement sur les données serveur existantes. Cache par identité/file/version ; nouvelle lecture quand le profil change ou le filtre de saison change. Aucun appel Riot supplémentaire, aucune activation du backfill. La couverture partielle reste visible. Le backend actuel fournit déjà cet endpoint ; aucun redéploiement requis.
- Frise : repère et coche sur la division courante ; couleur propre à chaque palier, notamment Maître violet, Grand maître rouge et Challenger doré. Aucun historique de promotions supposé.
- Runes : ouverture refusée si la fenêtre propriétaire est inactive/masquée ou si la cible n’est plus attachée. Fermeture et détachement de la cible sur désactivation, masquage, déchargement, déplacement de fenêtre et défilement extérieur. Le défilement interne des runes reste autorisé.

# Reprise — v38, 24 septembre 2026

Version active : **native-profile-v38**. Passe de disposition uniquement : largeur centrale plafonnée à 1080 unités WPF, classement 300 unités, trois cartes sur une même rangée (bilan/champions/rôles), historique immédiatement dessous. En-tête encadré avec actualisation, barre Résumé, fonds anthracite, cartes de match bleu/rouge. Filtres indépendants conservés ; portée en infobulle et statistiques détaillées repliées. Aucun changement de backend, de collecte ou de quotas. Les captures WPF utilisent des données synthétiques, aux largeurs 1050/1220/1920. Les contrôles vérifient aussi la rangée commune, la largeur maximale et la visibilité de l’historique.

Compiler `native/Build.ps1 -Check`, puis relancer `Lancer-Natif.cmd`. Le plan LP reste reporté après validation de cette disposition.

# Reprise — v37, 24 septembre 2026

Version active : **native-profile-v37**. DeepLoL a pu être inspecté dans le navigateur intégré. Anneau winrate bleu/rouge, barres verticales par rôle, moyennes K/D/A et route des divisions Solo/Flex ajoutés avec les données existantes. Aucun nouvel appel Riot ni tâche périodique. Le graphique LP reste à construire à partir de relevés serveur datés : voir [le plan détaillé](PROFILE-ROADMAP.md). Compilation : `native/Build.ps1 -Check`, lancement : `Lancer-Natif.cmd`.

# Reprise — v36, 24 septembre 2026

Profil réorganisé : identité agrandie, classement en colonne gauche, bilan horizontal à quatre indicateurs, champions et rôles côte à côte, historique sous les statistiques. Palette anthracite et accent menthe. Filtres indépendants, infobulles, détails des parties et pagination conservés. Aucun nouveau fournisseur, appel réseau ou calcul périodique. Pas de score IA ni de graphique LP sans données disponibles.

Version à compiler/lancer : **native-profile-v36** avec `native/Build.ps1 -Check`, puis `Lancer-Natif.cmd`. Les règles de configuration du lanceur v35 ci-dessous restent applicables. La référence web était inaccessible automatiquement ; la disposition s’appuie sur les captures DeepLoL fournies par l’utilisateur.

# Reprise — v35, 24 septembre 2026

La section de saisie de clé Riot est retirée de l’interface. `Lancer-Natif.cmd` et `Lancer-Serveur-Local.cmd` démarrent désormais tous deux le client serveur via `native/Start-Client.ps1`. Priorité : `RIFT_SERVER_URL`, sinon `RIFT_PORT`, sinon le port de `server/.env`, sinon 5080. La configuration réelle actuelle utilise 5081. La clé d’accès privée est lue depuis `RIFT_SERVER_ACCESS_KEY_FILE` ou `server/.secrets/access-key`. Sans ce fichier, le lancement explique comment initialiser le backend et ne bascule pas en mode local. La clé Riot se configure uniquement dans `server/.secrets/riot-key` côté serveur ; le client serveur ne relit plus son ancienne clé chiffrée Windows.

Compiler avec `native/Build.ps1 -Check`, puis lancer `Lancer-Natif.cmd`. Version active : **native-profile-v35**. Les instructions v34 ci-dessous restent valables pour démarrer Docker ; leurs remarques sur le lancement local implicite et le port 5080 imposé par le second lanceur sont remplacées par les règles ci-dessus.

# Reprise — 22 septembre 2026

## Version active

**native-profile-v34**, branche `main`. Application WPF .NET 10 ; profil et fluidité prioritaires. Direction active : mode client/serveur privé (ASP.NET Core, Quartz, PostgreSQL, Docker Compose). Le mode serveur ne contacte pas OP.GG.

- Collecte limitée aux **20 derniers matchs** : `RIFT_HISTORY_BACKFILL=false`. Anciens matchs et curseurs conservés, aucun rattrapage de saison automatique.
- KDA, bilan, champions et rôles calculés sur les parties chargées et filtrées, hors remakes. « Voir plus » agrandit cet échantillon avec les pages déjà stockées, sans appel Riot supplémentaire.
- Les compteurs victoires/défaites du classement Riot restent distincts de cet échantillon. Pas de limite de rétention à 30 jours déduite de DeepLoL.
- Actualiser conserve le profil affiché. Cooldown serveur partagé de **5 minutes** ; budgets privés : **5 actualisations acceptées et 3 nouvelles recherches / 10 minutes**. Consultations connues accessibles.
- Jobs récents distincts des jobs de saison, notifications SignalR, matchs mutualisés entre participants sans suivi récursif.
- Schéma PostgreSQL **v2** : ne pas relancer un ancien backend v31/v32 sur cette base.
- Overlay Ctrl+X, détachement Ctrl+Maj+X, déplacement, transparence, bannière ; détails des parties et profils cliquables ; diagnostic en direct.

## Sur le second PC : backend réel

Installer Git, le SDK .NET 10 Windows x64 compatible avec `native/global.json` (10.0.401) et Docker Desktop avec conteneurs Linux. Démarrer Docker Desktop. Dans un clone propre :

```powershell
git pull --ff-only
powershell -NoProfile -ExecutionPolicy Bypass -File native/Build.ps1 -Check
powershell -NoProfile -ExecutionPolicy Bypass -File server/Initialize-Dev.ps1
notepad .\server\.secrets\riot-key
```

Placer uniquement la clé Riot valide dans ce fichier. Si nécessaire, cloner auparavant avec `git clone https://github.com/Kalamare/RiftCompanion.git`, puis `cd RiftCompanion`.

Dans **la même fenêtre PowerShell** :

```powershell
$env:RIFT_FIXTURE_MODE = 'false'
$env:RIFT_PORT = '5081'
$env:RIFT_HISTORY_BACKFILL = 'false'
docker compose -p rift-real -f server/compose.yaml up -d --build
Invoke-RestMethod http://127.0.0.1:5081/health

$env:RIFT_SERVER_URL = 'http://127.0.0.1:5081/'
$env:RIFT_SERVER_ACCESS_KEY_FILE = "$PWD\server\.secrets\access-key"
.\Lancer-Natif.cmd
```

La santé doit indiquer `data: riot`. Pour conserver la configuration Compose, inscrire `RIFT_PORT=5081`, `RIFT_FIXTURE_MODE=false` et `RIFT_HISTORY_BACKFILL=false` dans `server/.env`.

**Le lanceur `Lancer-Serveur-Local.cmd` cible 5080**, pas 5081. Utiliser les commandes ci-dessus pour `rift-real`. Les variables PowerShell doivent être définies de nouveau dans une nouvelle fenêtre. Sans `RIFT_SERVER_URL`, `Lancer-Natif.cmd` utilise le mode local historique.

## Données non transférées par Git

`server/.secrets`, `server/.env`, le volume PostgreSQL, les exécutables, `.tools`, caches et profils de `%LOCALAPPDATA%\RiftCompanion` restent locaux. La clé chiffrée Windows ne se transfère pas entre PC.

Le second PC aura une nouvelle base, sauf transfert explicite de sauvegarde. Une sauvegarde antérieure à v33 existe uniquement sur le premier PC dans `server/.secrets/before-v33-*.dump` ; elle ne représente pas forcément le dernier état. Pour reprendre les données actuelles, réaliser une nouvelle sauvegarde PostgreSQL et la transférer séparément par un canal privé. Ne pas utiliser `down -v` si les données doivent être conservées.

## Validation et suite

Validation v34 : **172 contrôles métier et contrôles WPF**, compilation sans avertissement (`native/Build.ps1 -Check`) ; **37 contrôles PostgreSQL**, REST/SignalR et cooldown (`server/Check.ps1`, projet fictif isolé). Ces tests ne mesurent pas les FPS en jeu. Les contrôles saisonniers restent présents pour le mode historique optionnel.

Avant distribution publique : authentification et budgets par compte, TLS, droits Riot Production, rétention, sauvegarde/restauration opérationnelle et tests de charge. Le limiteur Riot reste conservateur. Aucun rang mondial/serveur saisi manuellement ni estimé depuis les LP.

Lire `server/README.md`, `docs/ACTUALISATION-QUOTAS.md`, `docs/ARCHITECTURE-PUBLIQUE.md`, `docs/PROFIL-PERSONNEL.md`, `docs/OVERLAY.md` et `docs/DIAGNOSTIC.md`.

Prompt de reprise : « Reprenons Rift Companion v34. Lis docs/REPRISE.md et docs/ACTUALISATION-QUOTAS.md. Vérifie Git, .NET et Docker. Backend réel rift-real sur 5081 ; collecte limitée à 20 matchs, statistiques sur les parties affichées, pas de rattrapage saisonnier. Les données et secrets du premier PC ne sont pas dans Git. Le profil et sa fluidité restent prioritaires. »
