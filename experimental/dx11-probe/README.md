# Banc DirectX 11 — Rift Companion

Prototype Windows x64 indépendant d'Overwolf et de Windows Game Bar. Il sert à
qualifier un moteur de rendu, pas à afficher les statistiques dans LoL.

## Deux modes distincts

**Archive CI `Rift-DirectX11-baseline-Windows` : DirectX seul.** Extraire le dossier
et lancer `RiftDx11Probe.exe`. Cette version ne contient pas GOverlay. Elle permet
de vérifier sur le PC le plein écran DXGI, le redimensionnement, Alt-Tab et la
recréation de swap chain. Elle ne démontre pas qu'un overlay fonctionne.

**Compilation locale avec GOverlay : panneau synthétique.** Depuis une console
développeur Visual Studio 2022, avec C++, SDK Windows, Git et CMake, à la racine :

```powershell
./experimental/dx11-probe/Build-WithGOverlay.ps1
./experimental/dx11-probe/build-goverlay/Release/RiftDx11Probe.exe --goverlay
```

Le script compile les sources épinglées et un adaptateur natif au protocole IPC
GOverlay. La DLL est chargée normalement **dans le processus du banc uniquement**.
Aucun injecteur, sélection de PID, accès Riot ou connexion à LoL n'est ajouté.
Fermer les autres démonstrations GOverlay avant ce test : l'amont utilise un nom
IPC fixe. Les pixels sont une image BGRA de 600×160, mise à jour une fois par
seconde dans une mémoire partagée protégée par mutex. Ce producteur fictif sera
remplacé par un adaptateur WPF après validation du rendu.

L'archive CI ne redistribue pas le moteur ni l'adaptateur lié à GOverlay : la
couverture des licences des dépendances est encore à clarifier dans
`experimental/goverlay/QUESTIONS-AMONT.md`. Le script conserve la licence amont
avec la compilation locale. Le lien « Powered by GOverlay » est cliquable dans
l'en-tête du banc. Les sources amont téléchargées restent dans
`experimental/goverlay/work/`.

## Manipulations

- **F11** demande ou quitte le plein écran via `SetFullscreenState`. Lire l'état
  **DXGI plein écran : OUI/NON**. Un refus n'est pas remplacé silencieusement par
  une fenêtre sans bordure. Les optimisations Windows peuvent encore affecter
  la présentation physique ; cet indicateur rapporte l'état DXGI, pas une mesure
  du chemin matériel de composition.
- **R** détruit puis recrée la swap chain et revient volontairement en fenêtré.
  Refaire F11 pour vérifier le retour au plein écran.
- **Espace** masque/réaffiche le panneau en mode GOverlay.
- **Échap** ferme le banc. Le processus se termine avec le moteur ; aucun
  déchargement à chaud n'est tenté pendant le rendu.
- Redimensionner la fenêtre et effectuer plusieurs Alt-Tab.

Le statut distingue la connexion IPC, le signalement du hook par le moteur et
les trames publiées. **Ces statuts ne prouvent pas que le panneau est visible.**
Vérifier visuellement le rectangle vert « PANNEAU GOVERLAY » et son compteur.
Un compteur figé après R/F11 est un échec à rapporter, même si le hook reste signalé.

## Mesures et recette

Les événements sont dans `%TEMP%/RiftDx11Probe-<PID>.log` : changements de focus,
demandes/état plein écran, dimensions, recréation et intervalle CPU p95 entre
images. Ce dernier inclut l'attente de présentation et les interruptions ; ce
n'est ni un temps GPU ni une mesure des performances de LoL.

Effectuer trois passages d'une minute, mêmes résolution et écran, en comparant
DirectX seul, GOverlay actif, puis panneau masqué. Noter Windows, GPU, résolution,
état DXGI, visibilité du panneau, continuité du compteur et fermeture normale.
Éviter de mélanger dans la comparaison les périodes d'Alt-Tab/redimensionnement.

## Contrôles automatiques

```powershell
./experimental/dx11-probe/Build.ps1
```

La CI compile le banc et le pont GOverlay. Le test `--smoke`, limité à 30 secondes
par CTest, utilise **WARP** (rendu logiciel), lit des pixels réellement rendus,
redimensionne la fenêtre puis recrée la swap chain. Il ne charge pas GOverlay,
ne teste pas le plein écran physique et ne permet aucune conclusion sur Vanguard.
Le contrôle distinct `--bridge-check` charge GOverlay dans le banc, attend la
connexion IPC pendant cinq secondes et vérifie l'arrêt du processus sous une
limite de 30 secondes. Il ne prétend pas vérifier la visibilité du panneau.

La recette interactive GOverlay et le portage WPF restent nécessaires. Même une
recette réussie sur ce banc ne certifierait pas la compatibilité avec LoL.
