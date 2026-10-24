# Qualification GOverlay

Voie retenue le 24 octobre 2026 : overlay indépendant d'Overwolf et de Windows
Game Bar. L'expérience Game Bar n'est plus la voie de développement retenue.
Ce dossier commence par une qualification du moteur natif ; il ne fournit pas
encore un overlay fonctionnel dans LoL.

## Sources vérifiées

Les révisions sont verrouillées dans `upstream.json`. Le dépôt principal pointe
vers le sous-module `hiitiger/game-overlay`, maintenant redirigé vers
`hiitiger/sln`. Le script vérifie que le commit natif correspond au sous-module.
La branche principale de GOverlay examinée se termine au commit du 5 février
2024 ; cela ne prouve pas l'absence de développements privés ou dans des forks.

- [GOverlay](https://github.com/hiitiger/goverlay/tree/3ca1f040b180dc6ce84baca093c6f726748461ad)
  annonce DirectX 9/10/11/12 et plusieurs frameworks, dont WPF. La démonstration
  livrée utilise Electron 16.0.10. Ce n'est pas un adaptateur WPF prêt à brancher.
- [Licence](https://github.com/hiitiger/goverlay/blob/3ca1f040b180dc6ce84baca093c6f726748461ad/LICENSE) :
  GPLv3 avec termes supplémentaires, dont un lien « Powered by GOverlay » sur
  chaque écran d'interface. Le champ MIT de `package.json` contredit cette
  licence racine ; ne pas le considérer comme une autorisation MIT du moteur.
  Il faut examiner la couverture du sous-module et des bibliothèques embarquées
  avant de distribuer une intégration. Une séparation en processus ne garantit
  pas à elle seule une exemption des obligations GPL.
- [Issue LoL #24](https://github.com/hiitiger/goverlay/issues/24) : crash plein
  écran rapporté, puis résolu selon son auteur en désactivant le débogage ; le
  problème de déplacement persistait. Ce n'est pas une validation actuelle.
- [Issue LoL #116](https://github.com/hiitiger/goverlay/issues/116) : saccades et
  crashs signalés, réponse du mainteneur annonçant une investigation, sans
  correctif confirmé dans les commentaires consultés.
- [Issue #127](https://github.com/hiitiger/goverlay/issues/127) : transitions
  fenêtré/plein écran avec changement de swap chain à tester explicitement.
- [Riot, 29 septembre 2026](https://support.riotgames.com/en-us/riot/performance/game-memory-access-removed-for-third-party-apps) :
  restrictions des accès mémoire des outils inconnus à partir du 6 octobre.
  Cette annonce ne certifie pas le fonctionnement de GOverlay. Les API de
  statistiques et la compatibilité d'un hook de rendu sont deux sujets distincts.

Aucune confirmation actuelle de compatibilité Vanguard n'a été trouvée dans les
sources consultées. Une DLL signée n'est pas à elle seule une approbation Riot.

## Vérification de compilation

Depuis une console développeur Visual Studio 2022 avec outils C++ x64 et SDK
Windows, à la racine du dépôt :

```powershell
./experimental/goverlay/Test-Build.ps1
```

Le script récupère les sources épinglées, compile `n_overlay` Release x64 et ses
dépendances, et conserve le journal et `reports/result.json`. Il ne lance aucun
injecteur, ne cible aucun jeu et ne copie aucun binaire dans le client WPF.
Les bibliothèques tierces déjà livrées par l'amont ne sont pas toutes reconstruites.
La CI publie les rapports uniquement. Une compilation réussie ne valide ni le
rendu, ni le plein écran, ni Vanguard.

## Suite et critères de passage

1. Obtenir une compilation native reproductible et documenter les adaptations
   nécessaires ; clarifier les licences et la distribution des dépendances.
2. Construire un banc DirectX 11 local, avec panneau fictif, attribution cliquable,
   visibilité basculable, mesure des temps de frame et aucun accès Riot. Vérifier
   plein écran réel via DXGI, Alt-Tab, redimensionnement, recréation de swap chain
   et arrêt. Le succès sur ce banc ne prouve pas la compatibilité LoL.
3. Définir l'adaptateur WPF : rendu bitmap borné, partage local des pixels et
   changements uniquement, cycle de vie et entrée clavier/souris. Les secrets et
   appels Riot restent dans les composants existants.
4. Établir la voie de compatibilité Riot/Vanguard, puis effectuer une recette
   dédiée LoL avant toute activation du moteur dans le lanceur normal. Si le
   chargement est bloqué, consigner le résultat ; aucune désactivation ni aucun
   contournement de Vanguard n'est prévu.
5. Porter les dix cartes et les raccourcis, puis comparer les temps de frame avec
   le client sans overlay sur plusieurs passages reproductibles.

L'application actuelle reste fonctionnelle en sans-bordure. Aucun succès de
compilation ou retour sur un autre produit n'est présenté comme un correctif
du plein écran de Rift Companion.
