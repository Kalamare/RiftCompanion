# Reprise — 20 septembre 2026

## État livré

Version active : **native-profile-v20**, branche `main`, lancement `Lancer-Natif.cmd`. Application native WPF uniquement ; l’ancien prototype web, ses tests et ses lanceurs ont été supprimés.

- Profil personnel, recherche et navigation vers les participants d’une partie, historique paginé et cache local.
- Fiche de partie compacte : rangs actuels datés, sorts, objectifs, bans barrés, K/D/A coloré, objets, balise et quête de rôle.
- Une seule rune sous le portrait. Popup détaillée au survol, défilable, fermeture automatique après sortie du pointeur ; toutes les runes primaires et secondaires restent consultables.
- Infobulles des objets : image, description, prix et composants directs affichés uniquement par leurs icônes, séparées par `+`.
- Portraits, sorts et runes livrés dans les assets ; images décodées hors UI, cache mémoire partagé.
- Fenêtre Diagnostic en direct : métriques du processus, requêtes, tâches, images, journal borné et analyse automatique avec seuils explicites.

## Sur le second PC

Installer Git et le SDK .NET 10 Windows x64 compatible avec `native/global.json` (10.0.401 ou correctif autorisé). Dans le dépôt existant, vérifier `git status` puis :

```powershell
git pull --ff-only
powershell -NoProfile -ExecutionPolicy Bypass -File native/Build.ps1 -Check
.\Lancer-Natif.cmd
```

Si le dépôt n’existe pas encore : `git clone https://github.com/Kalamare/RiftCompanion.git`, puis `cd RiftCompanion` avant de compiler. Le premier build nécessite Internet pour NuGet. Les scripts de mise à jour des assets nécessitent PowerShell 7, mais ne sont pas nécessaires pour compiler les assets déjà versionnés.

Les exécutables, `.tools`, rendus de test et caches ne sont pas versionnés. La clé Riot, chiffrée pour le compte Windows local, doit être renseignée à nouveau sur le second PC. Les profils, préférences et autres données de `%LOCALAPPDATA%\RiftCompanion` ne sont pas transférés par Git.

## Priorités et limites à conserver

Le profil et sa fluidité restent prioritaires. Les rangs mondial/serveur automatiques restent à intégrer avec une source validée : **aucune saisie manuelle, aucun classement estimé à partir du palier/LP**. Lire `CLASSEMENT-MONDIAL.md` avant ce chantier.

Les rangs participants et descriptions d’objets/runes utilisent les données actuelles, pas celles du patch historique. Les alertes du diagnostic sont des signaux indicatifs, pas une preuve automatique de fuite ou de bug.

Dernière validation v20 : compilation sans erreur ni avertissement, **136 contrôles métier et contrôles WPF réussis**. La recette sur le second PC doit notamment confirmer le survol/défilement des runes, les infobulles et les mises à l’échelle Windows. Les tests simulés ne mesurent pas les FPS en jeu ni la disponibilité réelle de Riot.

## Lecture de reprise

1. `README.md` et `native/README.md` : installation et commandes.
2. `docs/PROFIL-PERSONNEL.md` : décisions et historique des versions.
3. `docs/DIAGNOSTIC.md` : instrumentation, seuils et portée des métriques.
4. `docs/CLASSEMENT-MONDIAL.md` et `docs/FLUIDITE-PROFIL.md` : priorités restantes.

Prompt de reprise possible : « Reprenons Rift Companion. Lis docs/REPRISE.md, docs/PROFIL-PERSONNEL.md, docs/DIAGNOSTIC.md et docs/CLASSEMENT-MONDIAL.md. Vérifie Git et les prérequis, puis lance native/Build.ps1 -Check. Version active native-profile-v20, lancement Lancer-Natif.cmd. Le profil et sa fluidité restent prioritaires ; ne pas ajouter de saisie manuelle des rangs. »
