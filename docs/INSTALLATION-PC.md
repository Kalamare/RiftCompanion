# Installation privee sur Windows

Le backend tourne sur le meme PC que le client. Pas de domaine ni de port a ouvrir
sur Internet : l'API reste liee a `127.0.0.1:5081`, PostgreSQL reste interne a Docker.
Le PC et Docker doivent rester demarres pour utiliser le backend.

## Premiere installation

Installer Git, Docker Desktop (conteneurs Linux) et le SDK .NET 10.0.401 indique
dans `native/global.json`. Demarrer Docker Desktop. Dans PowerShell :

```powershell
git clone --branch codex/cloud-backend-handoff https://github.com/Kalamare/RiftCompanion.git
cd RiftCompanion
.\Lancer-Backend-Local.cmd
```

Pour un clone existant, conserver les modifications locales et recuperer la
branche avec `git fetch origin` puis `git switch codex/cloud-backend-handoff`.
Ne pas utiliser de reset destructif pour contourner un conflit.

Le lanceur initialise les secrets locaux manquants, demande la cle Riot avec une
saisie masquee si son fichier est vide, compile et teste le client, construit le
backend reel puis ouvre le client sur le bon port. Aucun secret n'est transfere
par Git ou par l'artefact GitHub. La cle configuree dans l'environnement cloud
n'est pas automatiquement installee sur le PC.

Le projet `rift-real` reutilise son volume existant s'il existe ; aucun volume
n'est efface. Sur une nouvelle installation, Docker cree une base vide.
Le rattrapage de saison est force a `false` et la collecte recente reste bornee
a dix matchs. Les secrets restent dans `server/.secrets`, exclus de Git.

## Validation reelle

Dans le client, rechercher son Riot ID sur EUW. Verifier successivement :

1. Identite puis niveau et rangs (un rang peut etre legitimement non classe).
2. Historique recent : au plus dix matchs collectes lors du premier chargement,
   eventuellement moins si Riot en retourne moins sur la periode configuree.
3. Details d'une partie, statistiques et indicateur de couverture partielle.
4. Actualisation : l'etat peut etre en cours ou en cooldown de cinq minutes ;
   ne pas multiplier les clics pour forcer une collecte.
5. Overlay en jeu : ouverture, fermeture, deplacement et absence de regression
   visible. Les tests WPF automatises ne prouvent pas les performances en jeu.

`/health` avec `data: riot` confirme le mode du backend, pas les droits de la cle.
En cas de refus 401/403, consulter les logs et verifier la cle dans le portail
Riot. La sortie reseau cloud a rencontre une page de blocage Cloudflare : cela
ne prouve pas que la cle est invalide ou que la connexion du PC sera bloquee.

Pour remplacer la cle locale : modifier `server/.secrets/riot-key`, puis recreer
API et worker avec le meme projet et le meme port. Le delai d'acces refuse deja
persistant peut durer quinze minutes ; ne pas supprimer la base pour l'annuler.

```powershell
$env:RIFT_PORT = '5081'
$env:RIFT_FIXTURE_MODE = 'false'
$env:RIFT_HISTORY_BACKFILL = 'false'
docker compose -p rift-real -f server/compose.yaml up -d --force-recreate api worker
```

## Utilisations suivantes

```powershell
.\Lancer-Backend-Local.cmd -SkipBuild
```

Omettre `-SkipBuild` apres une mise a jour. Pour arreter sans effacer la base :

```powershell
docker compose -p rift-real -f server/compose.yaml stop
```

Ne pas utiliser `down -v` sur le projet reel. Aucune sauvegarde de l'ancien PC
n'est requise pour une nouvelle installation ; conserver les donnees creees ensuite.

## Compilation GitHub

Le workflow `Windows native checks` compile et execute les controles metier/WPF.
Son artefact `RiftCompanion-Windows` contient le client compile et ses dependances
applicatives, mais exige encore le runtime .NET Desktop correspondant. Il ne
contient ni Docker, ni PostgreSQL, ni secrets. Le SDK installe ci-dessus fournit
le runtime necessaire. La compilation locale reste le chemin par defaut.
