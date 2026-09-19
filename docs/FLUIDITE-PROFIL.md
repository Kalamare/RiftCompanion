## Version actuelle : native-profile-v4

Le chargement complet avant affichage reste actif. Les emblèmes de rang sont désormais les PNG officiels Riot livrés avec le programme, décodés hors UI. La fenêtre se masque dès la fermeture ; annulation et nettoyage réseau se poursuivent en arrière-plan jusqu’à la sortie. Build sans erreur ni avertissement, 57 contrôles métier et contrôles WPF réussis. Le test synthétique de décodage observe un écart maximal de 22 ms entre ticks du dispatcher ; il ne mesure pas le délai réel de fermeture ni les performances en jeu.

Essai : lancer Lancer-Natif.cmd, charger un profil, vérifier les emblèmes, les suggestions après une première recherche, puis fermer pendant et après un chargement. Relancer pour contrôler la restauration de la clé. Les sections suivantes décrivent les versions précédentes.
# Priorité P0 : fluidité du profil

## Ajustement demandé : présentation complète après chargement

La version `native-ready` conserve les requêtes parallèles et le décodage hors du thread UI, mais ne publie plus les instantanés intermédiaires dans la vue. Une barre indéterminée animée affiche les étapes : recherche, parties, préparation des noms et images. Le profil reste masqué jusqu’à la fin de la préparation ; Render n’est appelé qu’une fois avant révélation. Un changement de filtre volontaire recalcule ensuite les statistiques normalement. Annulation et erreur laissent le profil masqué et arrêtent l’animation. Si certaines illustrations sont indisponibles, le traitement se termine avec un avertissement plutôt qu’un chargement infini.

Les emplacements sans objet conservent leur largeur et leur ordre, mais sont entièrement transparents, sans point et sans infobulle. Les icônes absentes pour un objet réel restent distinctes des emplacements vides.

Quinze pictogrammes vectoriels natifs couvrent rangs (insigne stylisé coloré selon le palier), cinq rôles, or, CS, combat, victoires, assistance, morts, participation, vision et balises. Ce sont des dessins intégrés, pas les emblèmes officiels Riot. Aucun téléchargement ajouté pour ces symboles. Les portraits et objets continuent à utiliser Data Dragon.

Validation : compilation sans avertissement ni erreur ; 53 contrôles métier et contrôles WPF passent, dont absence de glyphe sur emplacement vide et validité des 15 dessins gelés. Rendu visuel complet à confirmer par l’utilisateur. `Lancer-Natif.cmd` utilise désormais `artifacts/native-ready/RiftCompanion.exe`.

## Historique de la première optimisation

16 septembre 2026. Les données réelles et illustrations ont été confirmées par l’utilisateur. La fluidité devient prioritaire avant toute nouvelle fonctionnalité.

## Causes et corrections

- Le délai fixe de 1,3 seconde entre appels ajoutait environ 30 secondes sur un profil sans cache. Il est remplacé par trois appels simultanés maximum, avec un budget glissant conservateur par hôte : 18 appels/seconde, 90/deux minutes. Retry-After suspend les nouvelles requêtes. Ce budget ne couvre pas les autres programmes ni les autres instances.
- Les résultats déjà en cache apparaissent avant les nouveaux détails. Publication des matchs par lots de trois, statistiques provisoires identifiées. Annulation du lot sur erreur ; aucune répétition automatique après 429.
- Les lectures et décodages PNG sont déplacés hors du thread WPF. Miniatures gelées à 48 pixels, cache mémoire plafonné à 512 entrées, réutilisées entre chargements.
- Les icônes du cache sont publiées avant les téléchargements. Chaque image arrivée actualise sa propriété ; aucun remplacement des grilles à la fin des images.
- Les matchs sont ajoutés dans une collection observable sans recréer les lignes existantes. Les filtres restent utilisables pendant les téléchargements. Sélection des cellules avec couleurs sombres explicites.

## Validation

Compilation sans erreur ni avertissement. 53 contrôles métier passent, ainsi qu’un test WPF sans fenêtre. Ils vérifient notamment les quotas, la concurrence limitée, la publication progressive, le cache et les notifications d’images.

Dernier essai synthétique avec réponse de détail en 80 ms : premier lot 126 ms ; 20 matchs 1225 ms ; rechargement des matchs en cache 13 ms. Test de 160 PNG : 5 ticks du dispatcher pendant le décodage, intervalle maximal observé 17 ms. Ces valeurs ne mesurent ni les serveurs Riot réels, ni l’impact en jeu, ni le rendu complet de la fenêtre.

Reproduction : `native/Build.ps1 -Check`. Les chiffres varient selon la machine. Aucun accès à un profil réel ni aucune clé nécessaire pour les tests.

## Essai réel restant

Fermer les anciennes instances puis lancer `Lancer-Natif.cmd`, qui utilise `artifacts/native-fluid/RiftCompanion.exe`. Charger un profil, scroller et filtrer pendant l’arrivée des images, puis recharger le même profil pour comparer le cache. Vérifier également l’annulation. La durée réseau réelle et la fluidité de la fenêtre complète restent à confirmer ; aucun impact FPS nul n’est revendiqué.
