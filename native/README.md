# Rift Companion — application Windows

Application C# / .NET 10 LTS + WPF + SQLite. Le SDK local `.tools/dotnet`, lorsqu’il existe, est utilisé en priorité ; il n’est pas versionné. Sur un autre PC, installer le SDK compatible avec `global.json`.

## Lancer

Depuis la racine, double-cliquer sur **Lancer-Natif.cmd**. L'application s'ouvre dans sa propre fenêtre, sans navigateur et sans serveur HTTP local.

Pour commencer en démonstration :

```powershell
.\Lancer-Natif.cmd --demo
```

Le bouton « Voir une démonstration » permet également de basculer à chaud. « Revenir à LoL » reprend la connexion réelle. Fermer la fenêtre arrête le programme ; il n'y a pas de service caché ni d'icône de notification pour l'instant.

Les préférences sont enregistrées dans `%LOCALAPPDATA%\RiftCompanion\settings.db`. Le rôle préféré ne modifie pas le rôle attribué dans LoL. Dans « Connexion et stockage », le sélecteur permet de choisir le dossier contenant LeagueClient.exe si l'installation n'est pas détectée.

## Compiler et vérifier

```powershell
.\native\Build.ps1 -Check
```

La commande compile en Release et exécute 136 contrôles métier et des contrôles WPF (décodage en arrière-plan, pictogrammes, chiffrement DPAPI) sans framework de test externe. Le projet de contrôles utilise SQLite réellement, et simule les réponses LCU. `Build.ps1` n'installe pas le SDK ; sur une autre machine, installer le SDK .NET 10 correspondant à `global.json` ou une révision corrective compatible. `Lancer-Natif.cmd` utilise le runtime local s'il est présent ; sinon le runtime desktop .NET 10 doit être installé sur la machine.

Exécutable généré : `artifacts/native-profile-v20/RiftCompanion.exe`. Ce build dépend du runtime .NET 10 ; ce n'est pas encore un installateur ni une version autonome distribuable.

La clé Riot est enregistrée après un chargement réussi, ou avec « Enregistrer la clé », puis restaurée au lancement. Elle est chiffrée par Windows pour le compte utilisateur courant dans `riot-key.dpapi`. « Oublier la clé » supprime cette copie. L’expiration côté Riot reste inchangée. Les 20 derniers profils consultés sont conservés dans `recent-profiles.json` pour proposer jusqu’à six suggestions pendant la saisie. Ce n’est pas une recherche globale de comptes par préfixe.

À la fermeture, la fenêtre disparaît immédiatement ; l’annulation et le nettoyage se terminent ensuite avant la sortie du processus. Le délai réel reste à vérifier en utilisation.

## Tests manuels

1. Ouvrir le programme en mode démo : deux équipes, noms, bans et cinq rôles.
2. Changer le rôle préféré, fermer, relancer : vérifier la persistance SQLite.
3. Revenir à LoL : observer les transitions et la draft réelle ; vérifier l'absence de données de démo résiduelles.
4. Fermer la fenêtre et vérifier que RiftCompanion.exe a disparu du Gestionnaire des tâches.
5. Comparer LoL seul puis avec WPF, fenêtres visible et masquée, selon le protocole du plan.

La RAM et le CPU affichés sont des relevés ponctuels du processus WPF. Ils ne mesurent pas l'impact sur LoL et ne sont pas directement comparables au CPU « un cœur » du prototype Node : le WPF affiche le pourcentage de la capacité totale du PC.

## Statut fonctionnel

Connexion et préférences : implémentées. Profil individuel : première version avec démonstration, API Riot et cache SQLite ; validation réelle à faire. Voir ../docs/PROFIL-JOUEUR.md. Recommandations et équipement : différés. Pas d'automatisation du jeu, pas d'overlay, pas de capture vidéo.
