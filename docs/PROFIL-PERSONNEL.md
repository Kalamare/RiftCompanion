# Profil personnel par défaut — 18 septembre 2026

## Interface v11 — 18 septembre 2026

Version active : `artifacts/native-profile-v11`, via `Lancer-Natif.cmd`. Le build utilise ce nouveau dossier pour ne pas écraser la v10 pendant son utilisation.

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
