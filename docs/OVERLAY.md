# Overlay Electron

L’overlay actuel affiche cinq joueurs par équipe sur deux rangées : identité révélée, champion, sorts/rune disponibles, rang et statistiques sur les parties collectées. Les périodes 12 h et 30 jours portent sur cet échantillon ; elles ne constituent pas un historique exhaustif.

## Utilisation

Le bouton Overlay ou Ctrl+X active l’option si nécessaire et affiche un aperçu même hors partie. Sans joueurs détectés, un message d’attente reste visible. Un second clic masque la fenêtre. **Ctrl+X** affiche ou masque la fenêtre. Les pseudos révélés sont cliquables et ouvrent le profil dans la fenêtre principale ; les autres zones laissent passer les clics vers le jeu. Ctrl+X est réservé globalement tant que RiftCompanion est ouvert. Un échec d’enregistrement du raccourci est signalé. Le mode **sans bordure** est le parcours à tester ; les anciens raccourcis WPF Ctrl+X et Ctrl+Maj+X ne s’appliquent pas à Electron.

Les identités masquées et les bots ne déclenchent pas de recherche de compte. Une identité/rang indisponible reste indiqué comme tel. Les accès LCU/Live restent locaux ; la clé Riot reste sur le serveur.

## Plein écran exclusif

Une fenêtre Electron transparente au premier plan ne suffit pas à fournir un overlay dans le rendu exclusif du jeu. Cette fonction n’est pas livrée. La préférence utilisateur est un moteur indépendant, sans Windows Game Bar ; le passage à Electron ne vaut pas décision d’intégrer Overwolf.

| Dossier | État |
|---|---|
| [GOverlay](../experimental/goverlay/README.md) | Sources épinglées et compilation native vérifiée ; intégration produit et compatibilité LoL non qualifiées |
| [Banc DirectX 11](../experimental/dx11-probe/README.md) | Banc synthétique indépendant du jeu ; ne prouve pas le fonctionnement dans LoL |
| [Game Bar](../experimental/gamebar/README.md) | Ancien essai, voie abandonnée à la demande de l’utilisateur |

L’adaptateur de rendu Electron, le cycle de vie, les entrées, la licence et la compatibilité Riot/Vanguard restent à établir avant toute intégration au lanceur normal. Les affirmations sur les autres logiciels ne remplacent pas un test de Rift Companion.

## Recette restante

Vérifier visibilité/masquage, Alt-Tab, fermeture de LoL, changement de résolution, écrans de DPI différents et absence de vol de focus. Comparer les temps de frame du jeu avec et sans overlay. La position multi-écran et les performances réelles ne sont pas garanties par les tests automatiques de la grille.
