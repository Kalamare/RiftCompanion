# Overlay Electron — sans bordure et fenêtré

Décision du 27 octobre 2026 : l’overlay cible les modes **Sans bordure** et **Fenêtré** de LoL. Le développement du plein écran exclusif est abandonné. RiftCompanion reste une application Electron indépendante, sans Overwolf ni Windows Game Bar.

L’overlay affiche cinq joueurs par équipe sur deux rangées : identité révélée, champion, sorts/rune disponibles, rang et statistiques sur les parties collectées. Les périodes 12 h et 30 jours portent sur cet échantillon ; elles ne constituent pas un historique exhaustif.

## Utilisation

1. Dans LoL, ouvrir les réglages **Vidéo** et choisir **Sans bordure** ou **Fenêtré**.
2. Lancer RiftCompanion avec `Lancer-Electron.cmd` ou l’exécutable portable habituel.
3. Utiliser le bouton **Overlay** ou **Ctrl+X** pour afficher/masquer la fenêtre. L’action active l’option si nécessaire et permet un aperçu hors partie ; sans joueurs détectés, un message d’attente reste visible.
4. Cliquer sur un pseudo révélé pour ouvrir son profil dans la fenêtre principale. L’overlay se masque alors ; Ctrl+X permet de le réafficher au retour au jeu. Les autres zones laissent passer les clics vers le jeu.

Ctrl+X est réservé globalement tant que RiftCompanion est ouvert. Un échec d’enregistrement du raccourci est signalé ; le bouton reste utilisable. Les anciens raccourcis WPF ne s’appliquent pas à Electron.

Les identités masquées et les bots ne déclenchent pas de recherche de compte. Une identité/rang indisponible reste indiqué comme tel. Les accès LCU/Live restent locaux ; la clé Riot reste sur le serveur.

## Si l’overlay n’apparaît pas

Vérifier le mode vidéo de LoL, puis afficher l’aperçu avec le bouton Overlay dans RiftCompanion. Si le bouton fonctionne mais pas Ctrl+X, vérifier le message de conflit de raccourci. Le plein écran exclusif n’est pas pris en charge : changer le mode vidéo, sans lancer les anciens bancs expérimentaux.

## Recette sur le PC

Le fonctionnement en sans bordure a été confirmé par l’utilisateur. La recette complète ci-dessous reste à consigner sous Windows, dans **chacun des deux modes** :

- Afficher/masquer plusieurs fois avec le bouton et Ctrl+X, y compris lorsque LoL a le focus.
- Cliquer sur un pseudo : vérifier le bon profil et le masquage de l’overlay ; revenir au jeu et le réafficher.
- Cliquer hors des liens : vérifier que le jeu reçoit les clics normalement.
- Faire Alt-Tab et vérifier le retour au jeu sans focus bloqué.
- Redimensionner le jeu en mode fenêtré, changer de résolution et tester plusieurs écrans/DPI : vérifier le placement et la lisibilité des dix cartes.
- Terminer la partie, fermer LoL puis RiftCompanion : vérifier le masquage et l’arrêt des processus.
- Comparer les temps de frame du jeu avec et sans overlay selon [Validation](validation.md#mesures-en-jeu-à-consigner).

Le placement suit actuellement la zone de travail de l’écran principal, pas la fenêtre du jeu. L’ancrage au jeu, le multi-écran et les performances réelles restent à qualifier/améliorer ; les tests automatiques de la grille ne les garantissent pas.

## Archives du plein écran

Les essais [Electron hors écran](../experimental/electron-fullscreen/README.md), [DirectX 11](../experimental/dx11-probe/README.md) et [GOverlay](../experimental/goverlay/README.md) sont archivés, comme [Game Bar](../experimental/gamebar/README.md). Ils ne font pas partie du build ou du paquet Electron distribué. Leur code et leurs licences sont conservés pour référence ; le workflow DirectX est manuel uniquement. Aucun développement d’intégration LoL/Vanguard n’est prévu dans ce périmètre.
