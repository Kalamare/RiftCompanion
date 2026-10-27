# Prototype plein écran : Windows Game Bar

> ARCHIVE — voie abandonnée : l’utilisateur ne souhaite pas Windows Game Bar. Cette procédure décrit un ancien essai, pas l’installation actuelle. Voir [overlay Electron](../../docs/OVERLAY.md).

Ce prototype vérifie uniquement le rendu d'un panneau via l'API Windows Game Bar.
Ce n'est pas encore l'overlay Rift Companion complet. Aucune clé, aucun appel
Riot, aucun accès réseau, aucune injection dans LoL, aucune lecture de sa mémoire.
L'application WPF et le backend existants ne sont pas modifiés.

Cette voie est indépendante d'Overwolf, mais nécessite Windows Game Bar.
Le fonctionnement sur LoL en plein écran doit être confirmé sur le PC cible :
une compilation réussie ne suffit pas. Les API de fenêtre WPF ordinaires ne
fournissent pas cette intégration au rendu du jeu.

## Vérification immédiate sans installation du prototype

Dans une partie d'entraînement en plein écran, ouvrir Win+G, épingler le widget
Performances intégré, puis fermer Win+G. Vérifier qu'il reste visible et que
LoL reste en plein écran. Si ce test échoue, ne pas porter l'interface sur ce
moteur avant d'avoir identifié la cause.

## Construction

Projet expérimental UWP .NET 9 x64 ; SDK Windows 10.0.26100 et outils de
compilation Windows requis. La CI `Game Bar fullscreen probe` compile le projet.
La génération des icônes fait partie de la compilation. Depuis une console développeur Visual Studio, à la racine :

```powershell
msbuild experimental/gamebar/Rift.GameBarProbe.csproj /restore /t:Publish /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /p:AppxPackageSigningEnabled=false
```

Après compilation, exécuter `./experimental/gamebar/Prepare-Layout.ps1`.
La CI valide le dossier avec MakeAppx et publie `Rift-GameBar-Probe-Windows`.
Il contient l'exécutable autonome compilé en code natif et les fichiers Game Bar.

## Installation expérimentale sur le PC

1. Télécharger l'artefact de la CI et extraire **toute** l'archive dans un dossier
   permanent, par exemple `Documents/RiftGameBarProbe`. Game Bar doit être installé.
2. Ce prototype non signé nécessite le **Mode développeur** Windows (paramètres
   « Pour les développeurs »). L'installateur ne l'active pas automatiquement et
   n'importe aucun certificat. Ce n'est pas encore une distribution grand public.
3. Dans PowerShell ouvert dans le dossier extrait :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-Probe.ps1
```

4. Dans le jeu, ouvrir Win+G, puis le menu des widgets et choisir
   « Rift Companion — test plein écran ». Épingler le widget avant de fermer Win+G.
   Ne pas lancer directement l'exécutable pour valider la superposition.

Le script enregistre uniquement `Rift.GameBarProbe` pour l'utilisateur courant.
Il ne change ni les paramètres LoL, ni les secrets, ni la base, ni le client WPF.
Conserver le dossier extrait tant que le widget est installé. Pour le retirer :

```powershell
Get-AppxPackage -Name Rift.GameBarProbe | Remove-AppxPackage
```

Le Mode développeur peut ensuite être désactivé s'il n'est pas utilisé ailleurs.
La validation MakeAppx prouve la structure du paquet, pas son activation dans
Game Bar ni son affichage dans LoL : ces deux vérifications restent manuelles.

## Recette requise

1. Ouvrir le widget Rift Companion depuis Win+G pendant une partie d'entraînement.
2. Épingler le panneau et fermer Win+G : il doit rester au-dessus du jeu.
3. Revenir dans Win+G et utiliser le bouton : son compteur doit augmenter.
4. Vérifier Alt-Tab, retour au jeu, changement de résolution et fermeture du jeu.
5. Noter la version de Windows, le mode vidéo LoL et le résultat ; ne pas assimiler
   une fenêtre normale ou une capture d'écran à une validation du plein écran.

Après succès seulement : porter les cartes des joueurs, définir un échange local
authentifié avec l'application existante et intégrer les raccourcis. Ne pas exposer
les clés Riot ou backend dans un widget ou un fichier partagé non protégé.

## Sources vérifiées le 24 octobre 2026

- [Exemples Microsoft](https://github.com/microsoft/XboxGameBarSamples)
- [Adaptation .NET 9](https://github.com/chenx-dust/XboxGameBarModernSample)
- [Limites Game Bar](https://learn.microsoft.com/en-us/xbox/game-bar/known-issues)
- [Conditions Overwolf](https://dev.overwolf.com/ow-electron/getting-started/project-roadmap/) : approbation et monétisation imposées ; non retenu ici.
- [Riot : accès mémoire tiers](https://support.riotgames.com/en-us/riot/performance/game-memory-access-removed-for-third-party-apps) : ce prototype n'utilise pas d'accès mémoire au jeu.
