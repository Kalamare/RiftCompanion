# Rang mondial et serveur — recherche du 19 septembre 2026

## Besoin

Afficher automatiquement la position mondiale, la position sur le serveur et le Top %, pour Solo/Duo et Flex. Pas de saisie manuelle. Aucune estimation à partir du seul palier/LP présentée comme un rang exact.

## Résultat vérifié

La [FAQ Porofessor](https://porofessor.gg/fr/faq) indique que le rang combine les classements Riot de toutes les régions. Son créateur est également celui de LeagueOfGraphs. Aucune API publique officielle de LeagueOfGraphs n'a été identifiée lors de cette recherche. Une tentative HTTP directe sur la fiche publique a reçu 403 : cette source n'est donc pas exploitable de manière fiable par le client actuel. Aucun contournement n'est implémenté.

L'[API Riot](https://developer.riotgames.com/apis#league-v4) fournit les entrées classées, mais le client actuel ne dispose pas d'un index mondial permettant de déduire la position d'un joueur. Son rang dans une petite ligue ne correspond pas à son rang serveur. Additionner quelques distributions arrondies ne produit pas non plus un classement exact.

## Voie automatique retenue pour une future intégration

Un service d'indexation distinct du client WPF, alimenté par les classements Riot, ou un fournisseur qui autorise l'accès à son index. Prérequis : accès aux données, quotas adéquats et hébergement à déterminer. Rien n'a été souscrit ni déployé.

Le service devra couvrir chaque serveur et les deux files, stocker des instantanés datés, définir l'ordre des paliers/divisions/LP et les ex æquo, et ne publier le rang mondial que lorsque toutes les régions nécessaires sont couvertes. Le Top % requiert aussi la population classée de la même file et du même périmètre. Une collecte partielle ne doit jamais être affichée comme un rang mondial complet.

Le client devra faire une seule requête asynchrone par profil à ce service, avec annulation, délai limité et cache, sans ralentir l'affichage des matchs. Afficher la date de l'instantané ; conserver le dernier résultat daté hors ligne. La clé du service Riot reste côté serveur.

## État livré

Le lien vers la fiche LeagueOfGraphs reste disponible. L'affichage automatique mondial/serveur n'est pas livré faute de source automatisable validée. Les contrôles synthétiques ne valident pas une intégration réelle à LeagueOfGraphs.
