# Electron → GOverlay → DirectX 11

Adaptateur de développement Windows x64. Le renderer réutilise les dix cartes de
l’interface Electron, avec les données fictives des tests. Il produit des images
BGRA hors écran ; le banc les transmet à son moteur GOverlay. Ce n’est pas encore
un overlay utilisable dans LoL. Aucun processus de jeu n’est ciblé.

## Construire et lancer

Prérequis : Node.js 24, Git, Visual Studio 2022 avec outils C++ x64, SDK Windows
et CMake 3.24 minimum. Depuis une console développeur Visual Studio à la racine du dépôt :

```powershell
npm ci
.\experimental\dx11-probe\Build-WithGOverlay.ps1
node experimental/electron-fullscreen/build.mjs
.\node_modules\.bin\electron.cmd dist/fullscreen-probe/main.cjs --probe "$PWD\experimental\dx11-probe\build-goverlay\Release\RiftDx11Probe.exe"
```

Fermer les autres bancs GOverlay avant de lancer : le nom IPC amont est fixe.
Le producteur Electron est invisible ; la fenêtre à regarder est le banc DirectX.
Fermer le banc termine le producteur et nettoie les fichiers temporaires.

- F11 : demander le plein écran DXGI, vérifier le statut OUI.
- Vérifier les dix cartes et l’horloge « Trame Electron », qui doit continuer.
- Espace : masquer/afficher le rendu GOverlay.
- R : recréer la swap chain, puis F11 pour revenir en plein écran.
- Faire plusieurs Alt-Tab, changer la taille de fenêtre, fermer le banc.

Le panneau fait 1280×830, placé sous l’en-tête du banc et son lien Powered by
GOverlay ; utiliser de préférence 1920×1080 pour voir toutes les cartes. Le compteur prouve que les pixels sont renouvelés uniquement s’il
est effectivement visible dans le banc. L’indication IPC/hook seule ne le prouve pas.
Les clics sur les pseudos ne sont pas encore transportés par cet adaptateur.
Ctrl+X reste le raccourci du client normal ; Espace appartient au banc.

## Transport et vérifications

Cadence Electron limitée à deux images par seconde, consommation native à une
image par seconde. Les images identiques sont ignorées. Une seule écriture est
active ; les images intermédiaires sont abandonnées plutôt que mises en file.
Le fichier temporaire est remplacé atomiquement après écriture. Le lecteur natif
vérifie la signature, les dimensions, la longueur exacte et le numéro de trame,
puis copie les pixels sous le mutex GOverlay. Il conserve l’image précédente
si aucune nouvelle trame n’est disponible. Une trame malformée fait échouer le test.

Le répertoire temporaire est unique à chaque session. Le renderer est sandboxé,
sans Node, sans secrets, sans API Riot et sans accès au client LoL. Le préchargement
ne fournit qu’un substitut local pour les illustrations de profil.

Ajouter `--smoke` à la commande Electron effectue un test automatique : démarrage
du banc, connexion IPC, réception de plusieurs trames Electron et lecture des pixels
du panneau dans le tampon DirectX après présentation, redimensionnement et recréation.
Le test échoue si la zone du panneau ne contient que le fond. Le mode automatique
utilise WARP et un tampon conservé ; il ne valide ni le plein écran exclusif matériel,
ni les performances en jeu.
Le workflow DirectX construit le moteur localement et exécute ce contrôle ; il
ne redistribue pas le moteur GOverlay.

## Étape suivante pour LoL

Qualifier ce rendu sur Windows, puis transporter les entrées et les données
réelles. Le chargement du moteur dans LoL reste une intégration distincte à
établir avec les contraintes Riot/Vanguard. Aucun contournement de l’anti-cheat
ni succès de compatibilité LoL n’est annoncé. Les obligations de licence amont
restent celles détaillées dans `experimental/goverlay/README.md`.

## Panneau absent malgré IPC connecté

Le nom de fenêtre `StatusBar` sélectionne le panneau permanent du moteur épinglé.
Une fenêtre portant un nom libre est dessinée uniquement lorsque GOverlay
intercepte les entrées ; le banc n’active pas ce mode. Le correctif du 27/10/2026
utilise ce panneau permanent sans activer le mode global d’interception. Ce panneau
peut intercepter la souris dans sa zone ; les clics ne sont pas encore transmis à
Electron. Sa définition fournit aussi une marge de titre vide : le moteur attend
ce champ lors du survol, même pour un panneau non redimensionnable.

Après mise à jour, reconstruire avec `Build-WithGOverlay.ps1` puis relancer
le producteur Electron : reconstruire uniquement JavaScript ne met pas à jour
le pont natif. Le compteur « trames » mesure les copies vers la mémoire partagée,
pas les images effectivement affichées. Les contrôles automatiques de pixels ne
remplacent pas la vérification visuelle des cartes sur le matériel réel en fenêtre et en plein écran.

### Correctif du rendu vide (27/10/2026)

Direct2D expose une interface de compatibilité DirectX 10 sur notre périphérique
DirectX 11. Le moteur épinglé sélectionnait cette interface : notre patch
`../goverlay/patches/prefer-d3d11.patch` donne priorité à DirectX 11. Il reconnecte
aussi le moteur lorsqu’une nouvelle swap chain remplace celle de la même fenêtre.
Le banc réinitialise l’état Direct2D avant présentation. Le patch est appliqué
aux sources épinglées et son empreinte figure dans le rapport de compilation.

Après `git pull --ff-only`, reconstruire **le moteur et le banc** avec
`Build-WithGOverlay.ps1`, puis reconstruire et lancer le producteur Electron
avec les commandes du début de ce guide. Aucun changement de Node ou CMake
n’est nécessaire si la compilation précédente réussissait.
