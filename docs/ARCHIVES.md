# Archives documentaires

Le nettoyage du 25 octobre 2026 remplace les notes de session et les guides WPF contradictoires par une documentation centrée sur Electron. Les fichiers retirés restent consultables à la révision précédant ce nettoyage ; ils ne sont plus des procédures actuelles.

## Essais de plein écran abandonnés — 27 octobre 2026

Le périmètre produit est désormais limité aux modes sans bordure et fenêtré. Les dossiers `experimental/electron-fullscreen`, `experimental/dx11-probe` et `experimental/goverlay` conservent les sources, preuves et licences des essais ; ils ne sont ni intégrés au client ni à poursuivre pour LoL. Game Bar reste également abandonné. Le workflow DirectX est conservé pour exécution manuelle uniquement. Le lancement normal passe par `Lancer-Electron.cmd` ou l’exécutable portable.

## Où lire maintenant

- Installation PC, reprise et dépannage : [Electron](ELECTRON.md) et [serveur](../server/README.md).
- Stack et cible publique : [Architecture](ARCHITECTURE.md).
- Profil, sources, quotas et saison : [Données](DONNEES.md).
- Plan, backlog et prochaines fonctions : [Feuille de route](ROADMAP.md) et tableau Notion.
- Fluidité, diagnostic et tests : [Validation](validation.md), avec les limites du diagnostic Electron dans Architecture.

## Documents remplacés

- [ACTUALISATION-QUOTAS.md](https://github.com/Kalamare/RiftCompanion/blob/adcd1f98e4a870a37c6fc61731b1f82e2a63125d/docs/ACTUALISATION-QUOTAS.md)
- [ADR-001-STACK-WINDOWS.md](https://github.com/Kalamare/RiftCompanion/blob/adcd1f98e4a870a37c6fc61731b1f82e2a63125d/docs/ADR-001-STACK-WINDOWS.md)
- [ARCHITECTURE-PUBLIQUE.md](https://github.com/Kalamare/RiftCompanion/blob/adcd1f98e4a870a37c6fc61731b1f82e2a63125d/docs/ARCHITECTURE-PUBLIQUE.md)
- [BACKLOG.md](https://github.com/Kalamare/RiftCompanion/blob/adcd1f98e4a870a37c6fc61731b1f82e2a63125d/docs/BACKLOG.md)
- [CLASSEMENT-MONDIAL.md](https://github.com/Kalamare/RiftCompanion/blob/adcd1f98e4a870a37c6fc61731b1f82e2a63125d/docs/CLASSEMENT-MONDIAL.md)
- [CLOUD-TEST.md](https://github.com/Kalamare/RiftCompanion/blob/adcd1f98e4a870a37c6fc61731b1f82e2a63125d/docs/CLOUD-TEST.md)
- [DIAGNOSTIC.md](https://github.com/Kalamare/RiftCompanion/blob/adcd1f98e4a870a37c6fc61731b1f82e2a63125d/docs/DIAGNOSTIC.md)
- [FLUIDITE-PROFIL.md](https://github.com/Kalamare/RiftCompanion/blob/adcd1f98e4a870a37c6fc61731b1f82e2a63125d/docs/FLUIDITE-PROFIL.md)
- [INSTALLATION-PC.md](https://github.com/Kalamare/RiftCompanion/blob/adcd1f98e4a870a37c6fc61731b1f82e2a63125d/docs/INSTALLATION-PC.md)
- [OPGG.md](https://github.com/Kalamare/RiftCompanion/blob/adcd1f98e4a870a37c6fc61731b1f82e2a63125d/docs/OPGG.md)
- [PLAN-PROJET.md](https://github.com/Kalamare/RiftCompanion/blob/adcd1f98e4a870a37c6fc61731b1f82e2a63125d/docs/PLAN-PROJET.md)
- [PROFIL-JOUEUR.md](https://github.com/Kalamare/RiftCompanion/blob/adcd1f98e4a870a37c6fc61731b1f82e2a63125d/docs/PROFIL-JOUEUR.md)
- [PROFIL-PERSONNEL.md](https://github.com/Kalamare/RiftCompanion/blob/adcd1f98e4a870a37c6fc61731b1f82e2a63125d/docs/PROFIL-PERSONNEL.md)
- [PROFILE-ROADMAP.md](https://github.com/Kalamare/RiftCompanion/blob/adcd1f98e4a870a37c6fc61731b1f82e2a63125d/docs/PROFILE-ROADMAP.md)
- [REPRISE.md](https://github.com/Kalamare/RiftCompanion/blob/adcd1f98e4a870a37c6fc61731b1f82e2a63125d/docs/REPRISE.md)
- [SOURCES-STATISTIQUES.md](https://github.com/Kalamare/RiftCompanion/blob/adcd1f98e4a870a37c6fc61731b1f82e2a63125d/docs/SOURCES-STATISTIQUES.md)

Le README racine et les guides `server/README.md`, `native/README.md`, `docs/OVERLAY.md` et `docs/validation.md` ont également été réécrits ; leurs versions antérieures restent dans la même révision Git.

Les sources et obligations des assets sont conservées à proximité des fichiers concernés. Les essais `experimental/` gardent leurs notices et procédures propres, avec leur statut expérimental ou abandonné explicite. Aucune suppression de code, données utilisateur, secrets, assets ou volume PostgreSQL ne fait partie de ce nettoyage.
