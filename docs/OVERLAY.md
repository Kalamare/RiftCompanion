# Overlay natif — v30

## Utilisation

Depuis v28, compléments [OP.GG](OPGG.md) indépendants de Riot : rang serveur / Top %, statistiques saisonnières du champion lorsqu’il est présent, périmètre et dates au survol. Depuis v30, le fournisseur n’est plus nommé dans l’interface ; la synchronisation saisonnière du profil n’est pas déclenchée par l’overlay. Les bilans 12 h/30 j restent ceux de l’échantillon Riot. Mondial et Flex distinct restent indisponibles.

Correctif v27 : les entrées Live Client portant le suffixe `#BOT` observé en entraînement sont traitées comme bots, sans requête de profil Riot. Cette convention locale n’est pas une classification générale de tous les Riot ID. Les comptes absents n’arrêtent plus les autres joueurs ; les erreurs de clé/quota restent bloquantes pour la série. La grille est remplacée après préparation, sans passage par une liste vide, et réutilisée à la réouverture si les identités sont identiques. Une panne locale conserve les dernières cartes avec un avertissement explicite. Régression testée sur bots, joueur absent puis humain, et réouverture sans remplacement des cartes. Le clignotement observé en jeu reste à recontrôler : la conservation de la grille ne prouve pas l’absence de toute transition de composition Windows.

Depuis v25, la bannière est compacte (340 × 76 unités WPF). L’overlay attaché comme détaché se déplace en faisant glisser sa barre de titre, indiquée par une poignée et un curseur de déplacement. Masquer/réafficher conserve sa position pendant la session ; détacher/rattacher ou changer de fenêtre de jeu recalcule le placement. Le déplacement utilise les coordonnées physiques du pointeur, sans activation forcée du jeu ni boucle de rendu continue. Le glissement en jeu et entre écrans de DPI différents reste à valider sur le matériel.

Depuis v24, une bannière « Overlay disponible ! » apparaît en haut à droite à la première détection de la fenêtre de jeu avec le clavier actif. Elle rappelle Ctrl+X, permet une ouverture par clic et propose une croix de fermeture. Elle disparaît après 12 secondes, au passage dans une autre application ou à l’ouverture de l’overlay ; elle ne revient pas à chaque Alt+Tab pour la même fenêtre de jeu. Elle ne prend pas le focus et ne déclenche aucun chargement de profils. « Disponible » désigne l’accès à l’overlay, pas la fin du chargement des statistiques. Une nouvelle fenêtre de jeu peut déclencher un nouveau rappel.

Lancer Rift Companion avec `Lancer-Natif.cmd`, ouvrir LoL et commencer une partie. Choisir le mode **fenêtré sans bordure** dans LoL pour permettre la superposition d’une fenêtre Windows ordinaire.

- **Ctrl+X** affiche ou masque l’overlay.
- **Ctrl+Maj+X** le détache sur un autre moniteur ; une nouvelle pression le rattache au jeu. Si un seul écran est disponible, il devient une fenêtre indépendante sur cet écran.
- Les boutons **Overlay** et **Exemple overlay** sont disponibles dans la fenêtre principale. L’exemple est explicitement fictif et ne fait aucun appel réseau.
- En mode détaché, la fenêtre peut être déplacée, redimensionnée et minimisée. Le bouton Masquer la masque ; la fenêtre reste sans cadre Windows pour permettre la transparence. Fermer l’application principale ferme aussi l’overlay.
- Depuis v23, le clavier est reçu par Windows Raw Input (`RIDEV_INPUTSINK`) lorsque LoL ou la fenêtre overlay est au premier plan. L’inscription est retirée hors de ce contexte. Seuls les états de X et des modificateurs sont exploités, sans caractères ni journal de frappe. Ctrl+X garde son usage habituel ailleurs. Aucun événement clavier n’est bloqué ou réinjecté : désactiver le raccourci de Porofessor ou fermer ce logiciel pour éviter que les deux overlays répondent.
- Un overlay attaché est masqué quand le jeu perd le premier plan. Son affichage ne demande pas l’activation de sa fenêtre. Sa surface reçoit néanmoins les clics de souris : masquer l’overlay pour interagir avec le jeu derrière ses cartes.

Le plein écran exclusif n’est pas garanti : cette version n’injecte aucun rendu dans LoL. Elle utilise une fenêtre WPF avec transparence par pixel et cartes opaques au premier plan et les API Windows habituelles. Le placement utilise les coordonnées du moniteur du jeu et les zones de travail des écrans ; un manifeste PerMonitorV2 permet à WPF de suivre leur DPI. La vérification physique sur deux moniteurs de résolutions/DPI différents reste nécessaire.

## Données des cartes

La liste vient uniquement de `https://127.0.0.1:2999/liveclientdata/playerlist`, l’API officielle locale du jeu. Aucune clé Riot/LCU n’y est envoyée. La tolérance au certificat local est limitée à cette origine et à ce chemin, sans proxy ni redirection. Les identités volontairement absentes ne sont pas récupérées via `summonerName` ou une autre source.

L’overlay cible dix joueurs, avec cinq cartes par rangée. Les modes à plus de dix joueurs sont signalés comme indisponibles. Chaque carte peut afficher :

- Riot ID révélé, icône et niveau du compte ; portrait du champion, sorts et rune fondamentale livrés localement.
- Rang actuel Solo/Duo, LP et bilan classé issu de League-v4.
- K/D/A et taux de victoire historiques sur le champion, à partir des parties présentes dans l’échantillon.
- Bilans 12 heures / 30 jours et rôle principal, calculés sur cet échantillon.
- Étiquettes explicables lorsque trois parties au moins existent sur le champion : farm ≥ 7 CS/min, participation ≥ 65 %, victoires ≥ 70 %.

Les statistiques utilisent **les dix dernières parties chargées par joueur**, toutes files hors remakes. Les fenêtres de 12 h et 30 jours ne sont donc **pas exhaustives**. Une absence dans l’échantillon n’est pas assimilée à zéro partie réelle. Les rangs sont actuels, les images suivent le catalogue disponible. Le serveur est détecté depuis la LCU, jamais déduit du tag.

Les classements mondial/serveur, la maîtrise du champion, les badges propriétaires et les identités professionnelles de Porofessor ne sont pas intégrés. Aucune estimation de ces valeurs ni saisie manuelle n’a été ajoutée. L’overlay affiche des profils historiques, pas des informations cachées sur les adversaires ni des suivis de délais de récupération.

## Choix de performances

- Pas d’injection, de capture du jeu, de hook DirectX, de WebView, d’animation continue ou de callback à chaque frame.
- Un événement Windows de changement de fenêtre active gère les raccourcis. Une vérification de secours par seconde lit uniquement la fenêtre active ; si elle ne change pas et que l’inscription clavier fonctionne, aucun traitement supplémentaire. Un échec d’inscription Raw Input est retenté automatiquement et son code Windows est affiché. Le dernier raccourci reçu reste visible après Alt+Tab ; son activation apparaît aussi dans le diagnostic Overlay. Le timer est arrêté à la fermeture ; aucun sondage continu du clavier.
- Pendant l’affichage, une lecture locale toutes les 15 secondes, séquentielle et bornée à deux secondes. Sans changement d’identité/équipe/champion/rune, les cartes ne sont pas reconstruites.
- Masquer, quitter le premier plan en mode attaché ou minimiser annule les chargements et arrête ces lectures. L’instrumentation générale du reste de l’application garde son comportement précédent.
- Un joueur enrichi à la fois, avec le même client Riot et le même budget que le profil principal. Un chargement de profil utilisateur est prioritaire avant le prochain joueur. Match-v5 peut avoir trois requêtes simultanées au sein d’un joueur, comme le profil existant.
- Cache des profils en mémoire : dix minutes, vingt identités au maximum. Les parties utilisent aussi SQLite. Les quotas peuvent allonger un premier chargement de dix profils ; le traitement asynchrone ne signifie pas que les résultats sont instantanés.
- Portraits, sorts et runes locaux ; images gelées, décodées hors du dispatcher, cache mémoire réutilisé. Les icônes de compte sont téléchargées en arrière-plan si nécessaires.
- La fenêtre de diagnostic voit les requêtes `Live` et les scopes `Overlay`. Ne pas interpréter la durée de chargement comme du temps CPU.

Ces choix réduisent les sources de travail évitable, mais ne garantissent pas un impact nul. Windows peut devoir recomposer l’image lorsque l’overlay recouvre le jeu. L’affichage sur le second écran garde également un coût de rendu.

## Validation et comparaison sur le PC

Contrôles automatiques : parsing de la liste locale, conservation des sorts/runes, absence de récupération des identités masquées, fenêtres temporelles, absence de badges avec un échantillon insuffisant, transport local sans secret, grille fictive de dix joueurs sans réseau et annulation/arrêt au masquage.

Ces contrôles ne mesurent pas les FPS, le focus réel dans LoL ni le déplacement physique entre écrans. Pour la recette :

1. Vérifier les deux raccourcis dans LoL ; revenir à un éditeur de texte et vérifier que Ctrl+X conserve son usage normal. Vérifier le dernier raccourci reçu dans la barre principale après Alt+Tab. Maintenir X ne doit pas ouvrir/fermer en boucle ; Ctrl+Maj+X doit uniquement détacher/rattacher.
2. Vérifier Ctrl+Maj+X avec un/deux écrans, puis avec des DPI différents. Essayer Alt+Tab, minimisation, masquage et réouverture pendant un chargement.
3. Dans une partie d’entraînement reproductible, comparer LoL seul, Rift Companion avec overlay masqué, overlay attaché visible et overlay détaché. Garder les mêmes réglages, limite FPS et scène ; attendre la fin du chargement initial des profils.
4. Faire trois passages d’au moins une minute par situation, en alternant l’ordre. Relever les FPS et surtout les variations de temps de frame si un outil adapté est disponible ; noter CPU/GPU et mémoire. Refaire les mesures avec le diagnostic fermé, car il a son propre coût.

Sans ces mesures, on ne peut pas attribuer les ralentissements de Porofessor à une cause précise ni affirmer que cette version a de meilleurs FPS.

## Références

- [Riot — Live Client Data API](https://developer.riotgames.com/docs/lol#game-client-api_live-client-data-api)
- [Riot — intégrité du jeu](https://developer.riotgames.com/docs/lol#developer-api-policy_game-integrity)
- [Microsoft — Fullscreen Optimizations et composition des overlays](https://devblogs.microsoft.com/directx/demystifying-full-screen-optimizations/)
- [Microsoft — Raw Input et réception en arrière-plan](https://learn.microsoft.com/en-us/windows/win32/inputdev/about-raw-input)

Correctif v22 (21 septembre 2026) : identification directe de la fenêtre RiotWindowClass, événements de focus relus au moment du traitement pour éviter un état périmé, réessai automatique après libération du raccourci par Porofessor, et détail de l’erreur Windows (1409 : raccourci occupé). Fermer puis relancer Rift Companion via Lancer-Natif.cmd pour charger le correctif. La validation finale du raccourci doit être faite dans une partie en mode fenêtré sans bordure.

Correctif v23 : remplacement de RegisterHotKey/WM_HOTKEY par Raw Input/WM_INPUT après le signalement de touches sans effet en partie alors que le bouton manuel fonctionne en mode sans bordure. Le maintien de X, Ctrl gauche/droite, Maj et la perte de focus sont testés ; l'inscription et le retrait du clavier sont vérifiés auprès de Windows. Ces tests ne remplacent pas la validation physique en jeu. Relancer l'application via Lancer-Natif.cmd pour quitter l'ancienne v22.

Depuis v26, le fond entre les cartes est transparent ; les cartes restent opaques et les bandeaux utilisent un fond sombre à 90 % pour garder le texte lisible. La barre est compacte, les précisions complètes restent en infobulle. Les touches Ctrl et X de la bannière ont une hauteur de 24 et un texte centré verticalement. Aucun flou, ombre animée ou animation continue : la transparence peut toutefois augmenter le coût de composition, à mesurer en jeu. En mode détaché, le déplacement utilise toujours la poignée et le redimensionnement la poignée inférieure droite.
