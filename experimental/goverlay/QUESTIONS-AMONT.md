# Questions techniques à clarifier

Brouillons préparés pour la suite ; aucun message n'a été envoyé.

## Mainteneur GOverlay

Nous évaluons GOverlay pour l'overlay de statistiques d'une application WPF
Windows x64. Révisions étudiées : `goverlay@3ca1f040b180dc6ce84baca093c6f726748461ad`
et son sous-module natif `sln@ef6d261977d2c5b65cd6b675d8a8ea572af84312`.

- Existe-t-il une version maintenue avec compatibilité League of Legends/Vanguard
  vérifiée récemment ? Sur quels patchs, versions Windows et modes DirectX ?
- Existe-t-il un correctif pour les saccades LoL de l'issue #116 et pour les
  transitions de swap chain de l'issue #127 ?
- La GPLv3 et les termes d'attribution du dépôt principal couvrent-ils aussi le
  sous-module `hiitiger/sln` ? Comment interpréter le champ MIT du package ?
- Où trouver les licences et sources correspondantes des bibliothèques natives
  embarquées, notamment FW1FontWrapper et Effects11 ?
- Une interface native WPF documentée et maintenue est-elle disponible, ou faut-il
  écrire l'adaptateur au protocole IPC actuellement exposé via Node/Electron ?

## Riot Developer Support

Rift Companion utilise les API Riot et Live Client pour afficher des statistiques
historiques. Nous évaluons séparément un overlay de rendu DirectX 11 sous Windows
x64 avec GOverlay, sans lecture des structures mémoire du jeu ni automatisation.
Ce moteur charge néanmoins une DLL dans le processus et intercepte le rendu.

- Existe-t-il une voie prise en charge pour ce type d'overlay depuis les changements
  annoncés le 29 septembre 2026, et quelles exigences s'appliquent au chargement ?
- Existe-t-il une procédure de validation du composant et des binaires ?
- Si cette intégration de rendu n'est pas prise en charge, quelle interface
  officielle permettrait une superposition en plein écran exclusif ?

Une réponse sur les API de données seules ne validerait pas le moteur de rendu.
