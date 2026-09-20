# Profil personnel par défaut — 18 septembre 2026

> Pour reprendre sur un autre PC : [fiche de reprise](REPRISE.md).

## Composants compacts v20 — 20 septembre 2026

Version active : `artifacts/native-profile-v20`, via `Lancer-Natif.cmd`.

La section « Créé par » affiche uniquement les icônes des composants, séparées par un signe +. Les noms restent accessibles au survol et aux outils d’accessibilité. Le prix et la description de l’objet restent inchangés.

## Survols, recettes et analyse v19 — 20 septembre 2026

Version historique : `artifacts/native-profile-v19`, via `Lancer-Natif.cmd`.

- Runes : popup au survol, sans bouton Fermer. La molette et la barre de défilement restent utilisables. Sortir de l’icône et de la popup ferme celle-ci après 250 ms ; cette grâce permet de passer de l’une à l’autre. Le glissement du curseur de défilement conserve la popup jusqu’à la fin de la capture souris. Échap, clic ailleurs dans la fiche ou désactivation de la fenêtre ferment aussi la popup.
- Objets : infobulle commune au profil et à la fiche, avec grande icône, nom, description, prix total et composants directs de fabrication (icônes et noms). Les champs `gold.total` et `from` viennent du catalogue Data Dragon actuel. L’ordre et les composants répétés sont conservés, les images sont téléchargées une seule fois avec les autres illustrations. Aucune recette ni prix n’est inventé pour un champ absent. Il s’agit des composants directs, pas d’un arbre récursif complet.
- Diagnostic : onglet « Analyse automatique » et résumé visible. Analyse locale sur un maximum de 120 observations, avec preuves chiffrées et conseils ; CPU/allocations soutenus, tendance mémoire, retards UI, erreurs réseau, quotas, chargements prolongés et décodages lents. Les signaux sont indicatifs et se résorbent avec leurs conditions. Seuils et limites dans [DIAGNOSTIC.md](DIAGNOSTIC.md).

Validation : 136 contrôles métier et contrôles WPF, dont défilement/fermeture au survol, infobulle d’objet rendue, recette ordonnée avec doublons, préchargement dédupliqué des composants, pics transitoires, retour au calme, hausse mémoire et quota. Compilation sans erreur ni avertissement. Recette utilisateur sur des parties réelles toujours utile.

## Runes interactives et diagnostic v18 — 20 septembre 2026

Version historique : `artifacts/native-profile-v18`, via `Lancer-Natif.cmd`.

Une seule rune (la fondamentale) est visible sous le portrait. Le panneau complet s’ouvre au survol ou au clic : la molette agit sur ses descriptions, les contrôles restent cliquables et la fermeture se fait par Fermer, Échap ou clic extérieur. La hauteur s’adapte à l’espace de travail Windows. Les sélections primaires et secondaires restent toutes présentes dans le panneau. L’écart entre les statistiques et les objets passe à 24 unités WPF.

Le bouton « Diagnostic en direct », sur le profil et la fiche de partie, ouvre une fenêtre non modale. Voir [le guide du diagnostic](DIAGNOSTIC.md) pour les métriques, leur portée et l’instrumentation. Le bouton Débug existant conserve son rôle pour les messages techniques du profil.

Validation : compilation sans avertissement ni erreur ; 127 contrôles métier et contrôles WPF. Le panneau est testé dans une fenêtre native avec une description volontairement longue et un événement de molette, en vérifiant que le défilement avance et que le panneau reste ouvert. Le diagnostic est contrôlé avec des métriques réellement remplies, un journal borné/concurrent et l’arrêt de son timer à la fermeture. Les mesures synthétiques ne remplacent pas une recette Riot réelle.

## Finitions et infobulles v17 — 20 septembre 2026

Version historique : `artifacts/native-profile-v17`, via `Lancer-Natif.cmd`.

- Prototype web supprimé : `src`, `public`, `test`, `package.json`, `Demo.cmd`, `Lancer.cmd` et son ancien guide. La démonstration native et les contrôles .NET restent disponibles.
- Runes : rune fondamentale et première sélection de l’arbre secondaire sous le portrait. L’infobulle présente les quatre sélections primaires et les deux secondaires en deux colonnes, avec icônes et descriptions françaises Data Dragon livrées localement. Défilement pour les longues descriptions. Le cache des détails passe au schéma 4 pour préserver séparément les deux arbres ; les anciennes fiches restent lisibles hors ligne et sont enrichies lorsqu’une clé est disponible.
- Convention commune d’infobulles sombres : noms et descriptions des sorts/objets ; farming exact et par minute, participation, dégâts et vision au survol des statistiques condensées, dans le profil comme dans le détail. Les descriptions suivent le catalogue actuel, pas le patch historique ; les paramètres non renseignés par Riot restent explicitement indisponibles.
- K/D/A en vert/rouge/orange dans l’historique et la fiche. Bans circulaires avec barre diagonale. Zones de survol des liens ajustées au contenu du pseudo ou du portrait ; navigation clavier conservée.
- Portraits décodés à 120 px au lieu de 48 px, objets/sorts/runes à 64 px et icône personnelle à 192 px ; redimensionnement de qualité et cache partagé de bitmaps gelés. Aucun téléchargement supplémentaire au survol. Les sources matricielles restent limitées par leur résolution d’origine.

Validation : compilation sans avertissement ni erreur, 123 contrôles métier et contrôles WPF. Rendus synthétiques de la fiche à 960/1220/1600 px et des infobulles ; vérification des descriptions hors ligne, première/seconde rune secondaire, calculs par minute, durée nulle, résolution des portraits et zones cliquables. Le banc de rendu héberge le contenu des infobulles dans une fenêtre hors écran, car un ToolTip WPF détaché ne rend pas toujours entièrement son ScrollViewer. La recette sur une partie réelle reste à confirmer.

## Fiche compacte et navigation v16 — 19 septembre 2026

Version historique : `artifacts/native-profile-v16`, via `Lancer-Natif.cmd`.

Les joueurs occupent une ligne horizontale compacte : portrait/niveau, identité et rang, K/D/A/CS/or/participation/vision, puis inventaire. Les six objets restent dans leur ordre (`item0` à `item5`) sur deux rangées de trois ; une quatrième colonne contient la balise (`item6`) et, dessous, l’objet lié à la quête de rôle (`roleBoundItem`). L’absence de ce dernier champ est distinguée d’un emplacement vide ; rien n’est déduit du rôle. Les dégâts restent accessibles dans l’infobulle des statistiques.

Les runes sont lues depuis `perks.styles` : rune fondamentale et arbre secondaire visibles sous le portrait, sélections détaillées au survol. Les arbres sont identifiés par `primaryStyle`/`subStyle`, indépendamment de leur ordre JSON. 67 icônes de runes et d’arbres Data Dragon sont livrées localement ; mise à jour avec `native/UpdateRunes.ps1`, provenance dans `Assets/Runes/SOURCE.md`. Les anciens détails en cache sont enrichis au premier accès avec une clé, et restent lisibles sans clé.

Le pseudo et le portrait de chaque joueur sont des boutons accessibles au clavier. Le clic ferme la fiche, annule ses chargements, puis ouvre le profil sur le même serveur en résolvant son PUUID : un ancien pseudo ne bloque pas la navigation. Cette consultation ne remplace pas le compte personnel ; « Mon profil » permet d’y revenir. Les joueurs fictifs restent dans une navigation de démonstration hors ligne.

Les messages de préparation des illustrations et de catalogue de la fiche ne sont visibles que si « Débug » est activé dans la page principale au moment de l’ouverture. Les erreurs de chargement restent visibles dans tous les cas.

Validation : 120 contrôles métier et contrôles WPF, dont recherche par PUUID, refus d’une identité incohérente, liens des 10 noms et 10 portraits, navigation et retour au compte personnel, visibilité du diagnostic, disposition inventaire/runes et rendus à 960/1220/1600 px. Les réponses Riot sont simulées ; recette sur parties réelles restant à confirmer.

## Détails et images v15 — 19 septembre 2026

Version historique : `artifacts/native-profile-v15`.

- Deux sorts d’invocateur par joueur, extraits de Match-v5 et affichés sous son portrait avec nom français en infobulle. Les anciennes fiches sont enrichies au premier accès avec une clé ; sans clé, elles restent lisibles et les sorts inconnus ne sont pas devinés.
- Pictogrammes vectoriels locaux pour tours, dragons, barons, hérauts, inhibiteurs et larves. Les objectifs non fournis restent absents.
- Rangs **actuels**, avec emblème, division, LP et date : Flex pour une partie Flex, Solo/Duo pour les autres files (libellé explicite, notamment en ARAM). Chargement League-v4 par PUUID, indépendant des images, avec budget Riot partagé, délai de 25 secondes et cache SQLite d’une heure. Une erreur n’est jamais présentée comme « Non classé ». Un résultat ancien peut rester visible avec sa date si l’actualisation échoue. Aucun rang historique ou mondial n’est estimé.
- 173 portraits et 34 icônes de sorts officiels Data Dragon 16.18.1 livrés dans les assets (environ 5 Mo). Disponibles sans requête réseau et avant le chargement du catalogue dans la fiche. Les objets apparaissent progressivement ; six téléchargements CDN simultanés maximum (budget distinct des appels API Riot). Le cache mémoire des images est partagé entre profil et fiche, protégé contre les accès simultanés ; un échec de lecture n’y reste pas mémorisé indéfiniment.

Les portraits livrés suivent la version du paquet d’assets, et non le patch historique de chaque partie. Mise à jour explicite avec `native/UpdatePortraits.ps1` et `native/UpdateSpells.ps1` (PowerShell 7). Sources et attribution Riot dans `Assets/Champions/*SOURCE.md`. Documentation : [Data Dragon](https://developer.riotgames.com/docs/lol#data-dragon_other) et [League-v4](https://developer.riotgames.com/apis#league-v4).

Validation : 116 contrôles métier, contrôles WPF, rendu synthétique aux trois largeurs. Le premier test local a décodé les portraits et sorts de la fiche en 121 ms sans réseau ; ce chiffre n’est ni une mesure de chargement d’un profil Riot complet, ni un comparatif avec Porofessor. Validation des rangs contre Riot réel et ressenti utilisateur restant à confirmer.

## Détails des parties v14 — 19 septembre 2026

Version historique : `artifacts/native-profile-v14`. Un clic sur une carte de l’historique (ou Entrée/Espace au clavier) ouvre une fenêtre dédiée. Retour au profil ou Échap ferme cette fenêtre et conserve le filtre, les parties et la position de défilement. Le chargement en cours est annulé à la fermeture.

La fiche reprend les équipes côte à côte : Riot ID, champion, niveau dans la partie, rôle lorsque pertinent, K/D/A, CS, or, dégâts aux champions, vision, participation, sept emplacements d’objets, objectifs et bans disponibles. Le joueur du profil est surligné et son équipe apparaît en premier. Les sous-équipes sont conservées dans les modes qui les fournissent. Aucun rang historique, groupe prémade, sort ou rune n’est inventé ; les sorts et runes ne sont pas encore affichés dans cette version.

Les réponses Match-v5 déjà reçues pendant le chargement du profil alimentent un cache SQLite normalisé des détails (30 jours, région + identifiant de partie). Pour les anciennes entrées sans détail, un appel Match-v5 est effectué au premier clic, avec la clé Riot existante et les mêmes quotas. Une fiche déjà enregistrée est consultable sans clé. Les statistiques apparaissent avant les illustrations ; les images du cache sont utilisées d’abord, puis les téléchargements et décodages se font en arrière-plan avec un délai limité. Les images Data Dragon correspondent au catalogue actuel, pas nécessairement au patch historique. La détection personnelle est suspendue pendant la consultation pour éviter les rafraîchissements concurrents.

Sources : [Match-v5 Riot](https://developer.riotgames.com/apis#match-v5), [Data Dragon](https://developer.riotgames.com/docs/lol#data-dragon). Aucun appel supplémentaire aux classements des dix joueurs. Les rangs mondial/serveur restent soumis au travail décrit dans `CLASSEMENT-MONDIAL.md`.

Validation : 107 contrôles métier et contrôles WPF, dont réouverture depuis le cache sans clé, absence de requête supplémentaire lorsque le profil vient d’être chargé, erreurs 403/404/429, annulation à la fermeture et rendus synthétiques à 960/1220/1600 px. Recette réelle Riot/LCU et fluidité en jeu à confirmer. Le mode exemple fournit dix joueurs fictifs explicitement signalés comme démonstration.

## Interface v13 — 19 septembre 2026

Version historique : `artifacts/native-profile-v13`. Dossier distinct pour préserver une v12 encore ouverte.

Thème WPF commun dans `native/Rift.Desktop/Themes/Controls.xaml` : boutons arrondis, états de survol/appui/désactivation, focus clavier turquoise, menus déroulants sombres, barres de défilement discrètes dans les deux directions, onglets et sections repliables harmonisés. Le bouton Débug distingue son état actif. Aucun timer, effet graphique ou animation continue ajouté ; virtualisation de l'historique et chargement du profil conservés.

Validation : 94 contrôles métier, contrôles WPF avec le thème partagé, rendus du profil à 1050, 1220 et 1920 px. Une galerie synthétique dans une fenêtre hors écran vérifie la sélection et l'ouverture/fermeture des menus, les sections repliables et les commandes de défilement vertical/horizontal. `RIFT_UI_PREVIEW_DIR` permet de conserver les rendus. Ces vérifications ne mesurent pas les FPS en jeu. Recette utilisateur : survol, Tab/Entrée/Espace, menus au clavier, molette et glissement du curseur, puis Voir plus sur un profil réel.

Les rangs mondial/serveur automatiques restent à intégrer selon `CLASSEMENT-MONDIAL.md` ; aucune saisie manuelle ajoutée.

## Interface v11 — 18 septembre 2026

Version historique : `artifacts/native-profile-v11`. Le build utilisait ce dossier pour ne pas écraser la v10 pendant son utilisation.

Profil limité à 1400 px de largeur : classements Solo/Duo et Flex côte à côte, puis historique en cartes à gauche ; quatre statistiques principales, détails repliables, rôles et liste des champions à droite. Le niveau reste visible sous le pseudo. Les compteurs techniques, heures de chargement, états de cache/catalogue et détails d’échantillon sont dans un panneau masqué par défaut, accessible avec « Débug ». Les erreurs, l’absence de clé et les indications de premier lancement restent visibles.

94 contrôles métier ; vérifications WPF du diagnostic masqué/affiché et des erreurs visibles, plus rendus synthétiques à 1050, 1220 et 1920 px. Recette sur le profil réel à confirmer, notamment le défilement avec Voir plus.

Recherche de source pour les rangs : la FAQ officielle de Porofessor confirme le même créateur que LeagueOfGraphs et une agrégation des classements Riot de toutes les régions (https://porofessor.gg/fr/faq). Aucune API publique officielle documentée de LeagueOfGraphs identifiée. Cela ne prouve pas l’absence d’un accès privé. L’import automatique reste à réaliser ; le lien externe n’est pas une intégration des chiffres.

## Ajout : icône de joueur et accès aux classements

L’icône officielle du joueur apparaît à côté du Riot ID. `profileIconId` est obtenu par Summoner-v4 ou par le compte LCU, conservé lors de la pagination et dans la sauvegarde personnelle. Data Dragon fournit l’image `img/profileicon/{id}.png`, mise en cache avec les autres illustrations ; décodage hors UI à 96 px. Un identifiant absent reste inconnu (l’icône 0 est valide). Une nouvelle recherche efface l’ancienne image. Les sauvegardes antérieures sans ce champ restent lisibles.

Le classement mondial/régional et le Top % ne sont pas fournis par la réponse individuelle League-v4 intégrée. Aucune estimation à partir du palier/LP ni reprise des valeurs de la capture utilisateur. Un bouton ouvre la fiche LeagueOfGraphs dans le navigateur ; aucun chiffre tiers n’est importé. L’affichage natif de ces trois mesures reste à réaliser avec une source de classement intégrable et datée.

Source des icônes : https://developer.riotgames.com/docs/lol#data-dragon_other

Le profil personnel devient l’accueil. « Rechercher un autre joueur » est une section facultative ; « Mon profil » revient au compte personnel et « Actualiser mon profil » recharge ses statistiques.

## Détection et mémoire

- Au démarrage puis toutes les 15 secondes, hors chargement du profil : découverte du lockfile et deux lectures locales (`/lol-summoner/v1/current-summoner`, `/riotclient/region-locale`). La surveillance de draft reste suspendue sur le profil.
- Riot ID obtenu par `gameName` + `tagLine`. Serveur obtenu par `region`, jamais par le tag ou la langue. Serveurs actuellement pris en charge : EUW, EUNE, NA, KR.
- Seul un compte détecté dans LoL définit le profil personnel. Les recherches et les exemples fictifs ne le remplacent pas. Un changement de compte détecté mémorise le nouveau compte ; une recherche en cours reste affichée jusqu’au retour volontaire à « Mon profil ».
- `personal-profile.json` dans le dossier de données conserve identité, rangs, matchs, date de chargement et curseur de pagination. Aucune clé ni mot de passe LCU dans ce fichier. Les matchs restent également dans le cache SQLite existant.
- Sans LoL, le dernier profil personnel est restauré depuis le disque, avec illustrations déjà disponibles. Sans détection initiale, l’application invite à ouvrir LoL ; elle ne considère pas une recherche récente comme le compte personnel.

## Actualisation

La détection locale et la lecture du profil sauvegardé ne nécessitent pas de clé développeur. Les statistiques publiques continuent d’utiliser la clé Riot existante. À l’ouverture du profil personnel, une actualisation est tentée si la sauvegarde a plus de cinq minutes ou ne contient pas encore de statistiques ; le bouton permet une actualisation explicite. Aucune répétition automatique d’une actualisation échouée à chaque cycle local.

Le profil sauvegardé reste visible pendant cette actualisation. Les statistiques actualisées sont révélées ensemble après préparation des illustrations. Une erreur laisse le profil sauvegardé consultable. Pagination 20 puis +10 conservée, sans limite totale de 100.

## Vérification

Contrôles métier : détection simulée, routage indépendant du tag, client absent puis disponible, annulation, erreurs, sauvegarde/restauration de 115 matchs avec curseur et rang, séparation recherches/profil personnel, changement de compte, sauvegarde corrompue, refus des données de démonstration, illustrations sans réseau.

Contrôles WPF sans fenêtre : restauration sans LoL ni clé, recherche puis retour au profil personnel, conservation du profil après échec d’actualisation, arrêt du cycle de détection.

Recette réelle restant à effectuer : démarrer avec LoL connecté, fermer les deux logiciels puis relancer Rift Companion seul, consulter un autre joueur puis revenir à « Mon profil », changer de compte LoL. Les tests simulés ne valident ni ces réponses LCU réelles ni les performances en jeu.
