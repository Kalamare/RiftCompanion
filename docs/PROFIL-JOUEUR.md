# Profil individuel — première étape

**Priorité actuelle : fluidité.** Données et images confirmées par l’utilisateur. Voir [FLUIDITE-PROFIL.md](FLUIDITE-PROFIL.md) pour les corrections, mesures et limites de la nouvelle version `native-profile-v4`.

Décision du 16 septembre 2026 : prioriser le profil avant les recommandations de draft. Stack : C# / .NET 10 LTS, WPF, SQLite.

## Tester

1. Relancer `Lancer-Natif.cmd`, onglet Profil joueur.
2. « Voir un exemple » affiche des statistiques entièrement fictives. Les illustrations officielles sont téléchargées depuis Data Dragon au premier usage, sans clé API.
3. Pour un vrai compte, ouvrir « Connexion aux données Riot » et saisir une clé de développement obtenue sur https://developer.riotgames.com/ (validité 24 h). Ne pas partager la clé dans le chat.
4. Saisir `Pseudo#TAG`, choisir le serveur et charger. La durée dépend des réponses Riot et du cache local ; le profil est révélé une fois les données et images préparées. Annulation disponible.
5. Vérifier Solo/Duo, Flex, ARAM et l’état vide ; comparer les rangs et quelques parties avec le client LoL.

## Données et calculs

- Account-v1 : Riot ID → PUUID ; Summoner-v4 : niveau ; League-v4 : rangs et bilan classé actuels.
- Match-v5 : 20 derniers identifiants demandés, détails par match. Le filtre s’applique à cet échantillon, sans rechercher 20 parties supplémentaires dans la file choisie.
- Victoires : victoires / matchs exploitables. Remakes exclus des agrégats et conservés dans l’historique.
- KDA global : somme(kills + assists) / max(1, somme(morts)).
- CS : sbires + monstres ; CS, dégâts champions et or par minute : total / durée totale en minutes.
- Participation : moyenne des (kills + assists) / kills de l’équipe ; équipe sans kill exclue de cette moyenne.
- Vision, kills, morts, assists et balises : moyennes par partie. Balises de contrôle : achats, pas placements.
- Rôles : position d’équipe Riot uniquement sur la Faille ; sinon « Non déterminé ».
- Le bilan classé Riot est distinct de l’échantillon récent. Aucune statistique de saison déduite des 20 parties.

## Performances et stockage

Chargement manuel, trois appels simultanés maximum et affichage final unique, budget glissant conservateur et arrêt sur HTTP 429 avec respect de Retry-After. Cache SQLite 30 jours des matchs normalisés du joueur sélectionné ; pas de réponse complète contenant les dix joueurs. Clé enregistrée sous forme chiffrée par DPAPI pour le compte Windows courant ; restauration automatique et bouton de suppression. Son expiration Riot reste inchangée. Pas d’actualisation du profil en arrière-plan. Surveillance LCU suspendue quand l’onglet profil est actif.

## Validation et suite

Compilation sans erreur ni avertissement ; 36 contrôles automatisés au total, dont 14 nouveaux contrôles sur le profil, ses calculs, SQLite, le routage et les erreurs Riot. API simulée pour les tests ; appel authentifié réel et performances en jeu restent à valider.

Ensuite : enrichir l’historique (objets, détails), ajouter les illustrations Riot et approfondir les statistiques utiles au joueur. Historique saisonnier, rang moyen des adversaires et badges comportementaux demandent des données et critères supplémentaires. Synergies et recommandations de draft différées.

## Illustrations et noms officiels

Le catalogue Data Dragon français résout championId vers le nom officiel (ex. 62 → Wukong, code interne MonkeyKing). Les portraits apparaissent dans les champions joués et les matchs ; sept slots montrent les objets de fin de partie avec noms au survol. Les identifiants item0 à item6 sont conservés dans leur ordre, y compris les slots vides. Les anciens matchs en cache sont rechargés une fois car ils ne contenaient pas ces identifiants.

Catalogue actuel, version affichée : les illustrations ne prétendent pas reconstituer le patch historique. Catalogue contrôlé toutes les 24 heures, fichiers versionnés dans riot-assets, trois téléchargements simultanés au maximum et uniquement pour les icônes nécessaires. Miniatures WPF décodées à 48 pixels et réutilisées en mémoire. Une indisponibilité des illustrations ne supprime pas les statistiques ; les icônes manquantes ont un remplacement textuel.

45 contrôles automatisés passent. Les données réelles du profil ont été confirmées correctes par l’utilisateur. La vérification visuelle des nouvelles illustrations a été interrompue par l’utilisateur avec Échap : rendu des images à confirmer. Relancer Lancer-Natif.cmd (build artifacts/native-assets) puis recharger le profil. Les sorts, runes et portraits des dix participants ne font pas encore partie de cette étape.

Source : https://developer.riotgames.com/docs/lol#data-dragon

## Mise à jour du profil v4

- Emblèmes officiels inclus dans l’application depuis ranked-emblems-latest.zip ; aucun téléchargement lors de leur affichage. Source et attribution : native/Rift.Desktop/Assets/Ranks/SOURCE.md.
- Grands nombres : séparateur de milliers « . », décimales « , », abréviations k et M. Les dégâts gardent une valeur exacte au survol et le tri reste numérique.
- Historique persistant des 20 derniers profils consultés, suggestions par début de Riot ID (six maximum), avec sélection du serveur. Cette première version ne recherche pas de nouveaux comptes par préfixe dans une base globale.
- Fermeture : masquage immédiat de la fenêtre, annulation asynchrone et libération des clients réseau en arrière-plan avant la sortie du processus.
- LP par match : non implémentés. Match-v5 ne fournit pas ce delta historique. Un suivi futur devra observer le classement avant/après, associer les observations aux parties et gérer les données manquantes ainsi que les changements de palier ; aucune valeur inventée pour les anciennes parties.

Validation actuelle : compilation sans avertissement ni erreur, 57 contrôles métier et contrôles WPF réussis, dont persistance de l’historique, formats numériques et chiffrement/restauration/suppression d’une clé de test. Les anciennes sections de validation retracent les étapes précédentes. Données et illustrations déjà confirmées par l’utilisateur ; nouveau rendu des emblèmes et délai de fermeture réel à confirmer. Lancer-Natif.cmd cible artifacts/native-profile-v4.

## Icônes officielles — native-profile-v5

CS utilise désormais le sbire officiel Data Dragon, or la bourse, combat/KDA les épées et participation le symbole champion. Vision et balises utilisent le Totem de balisage ; les balises de contrôle utilisent leur propre objet. Les cinq rôles utilisent les PNG du pack ranked-positions officiel (variante dorée, décorative).

Les ressources de tableau de scores restent en version 5.5.1, celle explicitement documentée par Riot ; les balises ont été récupérées en 16.18.1, dernière version renvoyée lors de l’intégration. Ces fichiers légers sont inclus dans le programme, décodés en arrière-plan avant révélation du profil et réutilisés en mémoire. Sources détaillées : native/Rift.Desktop/Assets/Ui/SOURCE.md. Les pictogrammes victoires, morts et assists restent locaux faute d’équivalent dédié identifié dans ce catalogue.

Lancer-Natif.cmd pointe vers artifacts/native-profile-v5. Contrôles WPF supplémentaires : présence des bitmaps officiels pour 12 usages, objets gelés et réutilisation sans redécodage. Rendu visuel final à confirmer dans l’application.

## Taille des icônes et historique étendu — native-profile-v6

Les icônes de statistiques occupent désormais une boîte de 24 px avec libellés de 12 px. Les marges transparentes des PNG d’interface sont retirées en mémoire avant affichage (proportions conservées, fichiers Riot inchangés). Le cadrage est calculé en arrière-plan et mis en cache ; portraits, objets des matchs et emblèmes ne sont pas recadrés.

Le sélecteur « Dernières parties » propose 20, 50 ou 100. Choisir une valeur puis « Charger le profil » : liste et statistiques sont remplacées ensemble une fois les données et illustrations préparées. Les filtres s’appliquent à l’échantillon choisi. Le service valide la plage 1–100 et demande ce nombre d’identifiants ; moins de parties peuvent être disponibles. Les détails déjà en cache sont réutilisés. La limite de trois requêtes simultanées et le budget Riot restent actifs : 100 parties sans cache peuvent nécessiter une attente de quota supérieure à une minute. Annulation toujours disponible. Le jeu de démonstration reste de taille fixe.

Validation : compilation sans avertissement ni erreur, 62 contrôles métier et contrôles WPF réussis, dont requêtes 20/50/100, absence de nouveau téléchargement des détails en cache, tailles invalides rejetées, cadrage alpha et séparation du cache des originaux. Rendu visuel réel à confirmer. Lancer-Natif.cmd cible artifacts/native-profile-v6.

## Pagination et types de parties — native-profile-v7

Le sélecteur 20/50/100 est retiré. Le premier chargement demande 20 identifiants, puis « Voir plus · 10 parties » demande uniquement la page suivante. Pas de plafond total de 100 dans cette interface ; arrêt sur une page incomplète ou vide renvoyée par Riot. Les exclusions (parties personnalisées, données indisponibles), les doublons ou les filtres peuvent donner moins de dix lignes nouvelles. La borne endTime reste fixe pendant la consultation pour réduire le décalage dû aux nouvelles parties. Une nouvelle recherche actualise cette borne et le classement.

Compte et classement sont réutilisés pour les pages suivantes, détails en cache conservés, trois requêtes simultanées maximum, budgets et Retry-After inchangés. Le bouton est désactivé pendant le chargement ; la page précédente reste visible et les statistiques sont recalculées une seule fois à la fin. En cas de problème, le curseur et le profil précédent sont conservés pour réessayer.

Catalogue intégré : 99 files documentées dans https://static.developer.riotgames.com/docs/lol/queues.json (copie du 17 septembre 2026), complété par 710 → Classée · 5v5 d’après les captures utilisateur ; cette entrée manque encore dans le catalogue téléchargé. Libellés français pour les modes courants, description officielle pour les autres, catégorie classée / normale / coop / tournoi. Un futur identifiant inconnu est explicitement signalé et son type n’est pas inventé. Les filtres suivent les files effectivement présentes.

Rôles : teamPosition en priorité, individualPosition en secours sur la Faille. Aucun rôle déduit du champion. « Rôle non fourni par Riot » est distinct de « Sans rôle standard » pour les autres modes. Une capture de la répartition ne permet pas d’identifier la partie concernée ni d’affirmer qu’il s’agit de l’outil d’entraînement. Les anciennes entrées en cache sans rôle ne contiennent pas individualPosition et ne sont pas re-téléchargées uniquement pour ce secours.

Validation : 68 contrôles métier et contrôles WPF, compilation sans erreur ni avertissement. Tests de pagination avec 115 matchs, fin de liste, reprise après erreur, absence de doublons et conservation de la borne temporelle. Rendu et API réelle restent à confirmer pour cette version.

## Catalogue du client et modes sans rôles — native-profile-v8

Les noms et groupes sont lus automatiquement dans le catalogue français extrait du client, distribué par CommunityDragon : https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/fr_fr/v1/queues.json. Il s’agit d’un miroir communautaire, distinct de l’API Riot. Une copie réduite de 420 entrées est intégrée pour le premier lancement hors ligne. Une actualisation publique sans clé est tentée au chargement du profil, au plus une fois par session et si le cache a plus de 24 h. Timeout 3 s, cache disque, conservation des noms intégrés/en cache en cas de panne. Le catalogue officiel queues.json reste un secours pour les modes historiques absents.

Le catalogue actuel distingue 1740 « Arena courage » et 1750 « Arena 3x6 » : l’étiquette commune affichée par un autre companion ne fait pas autorité sur cette distinction. Les nouveaux identifiants du catalogue peuvent être interprétés sans modification du code. Un identifiant absent des deux sources reste explicitement inconnu.

La répartition par rôle utilise uniquement le groupe kSummonersRift du client (secours explicite pour certaines anciennes files). ARURF, URF, Arena, ARAM et autres modes alternatifs sont exclus de cette section, même si un ancien match en cache porte un rôle. Leurs matchs restent dans l’historique, les statistiques globales et les champions joués. Le filtre par mode ne crée donc plus une ligne « Rôle non fourni » pour l’ARURF ; une section sans partie éligible affiche un message approprié. Pour une vraie partie classique sans position, le libellé honnête « Rôle non fourni par Riot » reste possible.

Validation : compilation et contrôles du catalogue, cache, mode hors ligne et exclusion ARURF ; Lancer-Natif.cmd cible artifacts/native-profile-v8. Les classements de placement Arena (1er, 4e, etc.) ne sont pas ajoutés dans cette étape.

## Libellés et stabilité du défilement — native-profile-v9

Les noms du client contenant déjà « Classé » sont affichés directement, sans préfixe supplémentaire. Pour « Voir plus », le panneau de chargement en haut reste masqué ; le bouton indique le chargement et reste focalisable. Les clics concurrents sont ignorés. Les demandes automatiques de mise en vue des contrôles reconstruits sont neutralisées pendant cet ajout, puis la position de la page juste avant le rendu est restaurée après mise en page. Les déplacements volontaires pendant la requête sont ainsi conservés. Le comportement est également appliqué à la sortie en erreur/annulation.

Compilation sans avertissement ni erreur, 74 contrôles métier et contrôles WPF existants réussis. Le défilement dans la fenêtre réelle reste à confirmer par essai utilisateur. Lancer-Natif.cmd cible artifacts/native-profile-v9.
