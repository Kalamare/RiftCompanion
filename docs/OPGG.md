# Compléments OP.GG — v30

Depuis v30, l’interface ne montre plus le nom du fournisseur. Les statistiques par mode manquantes sont calculées via une synchronisation Riot indépendante, décrite dans [SOURCES-STATISTIQUES.md](SOURCES-STATISTIQUES.md). Les sections ci-dessous conservent l’historique des intégrations et leurs limites.

## Affichage

Le profil reçoit un bloc **rang serveur OP.GG / Top %** et un volet repliable **Champions de la saison**. Le palier et les LP Riot restent inchangés. Le filtre des matchs Riot ne filtre pas les statistiques OP.GG : leur saison et leur file sont indiquées séparément. `RANKED` est affiché comme « Classé (périmètre OP.GG) », sans le confondre avec Solo/Duo.

Dans l’overlay, le rang serveur est ajouté sous le rang Riot. Si le champion est présent dans la liste OP.GG, son nombre de parties, son taux de victoire et ses K/D/A moyens proviennent de cette liste saisonnière. Les bilans 12 h/30 j, le rôle principal et les étiquettes restent calculés sur l’échantillon Riot. Les dates, le serveur et le périmètre OP.GG sont disponibles au survol. En l’absence du champion dans la réponse, les statistiques récentes Riot sont conservées ; l’absence n’est pas interprétée comme zéro partie.

Le rang mondial et un rang régional Flex distinct ne sont pas fournis. Le rang régional reste un complément attribué à OP.GG, sans affectation arbitraire à une file. La liste de champions peut être partielle (dix champions dans la réponse réelle testée). Les compteurs de kills/morts/assists sont divisés par les parties pour produire les moyennes.

## Fraîcheur et performances

Le cache local dure une heure, est partagé entre profil et overlay et survit au redémarrage. Un profil rouvert après expiration tente une actualisation ; aucune boucle horaire permanente n’est ajoutée. La date de récupération et la date du profil source sont distinctes. Une récupération récente ne garantit pas des statistiques source récentes. La date source n’est pas une date certifiée de calcul du ladder.

Les chargements sont asynchrones, annulables au changement de profil, au masquage de l’overlay ou à l’arrêt. OP.GG ne bloque ni la construction du profil Riot ni son chargement de matchs. Le client partage une file séquentielle (une requête OP.GG à la fois), un délai de 12 secondes et une limite de réponse de 1 Mo. Aucun téléchargement d’image supplémentaire. Seuls les champs utilisés sont demandés.

HTTP 429 respecte Retry-After, avec deux minutes par défaut. Les autres erreurs suspendent les appels pendant une minute. Une erreur renvoie le cache ancien daté lorsqu’il existe, sinon un état indisponible. Aucun essai en boucle pour les dix joueurs. Les bots et les démonstrations ne consultent pas OP.GG.

## Contrat technique

Endpoint officiel : `https://mcp-api.op.gg/mcp`, méthode `tools/call`, outil `lol_get_summoner_profile`. Seuls Riot ID et région, déjà utilisés pour rechercher le joueur, sont transmis ; aucune clé Riot, aucun secret LCU. Pas de modèle IA ni runtime PHP. Le diagnostic utilise la catégorie OP.GG, sans conserver les identités ni URL.

Le serveur testé renvoie un texte compact avec déclarations de classes et valeurs imbriquées dans l’enveloppe JSON MCP. Le décodeur interprète des données, jamais du code : taille, profondeur et nombre de nœuds bornés, arité et champs dupliqués vérifiés, identité et région contrôlées. Les identités manquantes et structures inattendues rendent le complément indisponible. Les nombres de parties incohérents sont exclus.

Cache : `%LOCALAPPDATA%/RiftCompanion/opgg-cache`, fichiers nommés par empreinte identité/serveur, contenu de profil public non chiffré, au maximum 200 entrées. Les réponses stockées repassent par la validation avant affichage. Cette intégration dépend du service public hébergé et de son format ; elle ne garantit ni disponibilité ni quotas futurs. La licence du dépôt n’est pas un engagement de service.

## Vérifications

Tests : format compact, identité/région incorrectes, schema invalide, rang absent, moyennes saisonnières, cache une heure, conservation hors ligne sur quota, arrêt des appels pendant cooldown, annulation, bots, affichage daté et indépendance des résultats Riot/OP.GG. Les vérifications ordinaires utilisent des réponses simulées, sans réseau OP.GG.

Références : [serveur officiel](https://github.com/opgginc/opgg-mcp), [rythme de mise à jour annoncé](https://help.op.gg/hc/en-us/articles/31089172140441-My-summoner-ranking-isn-t-updating). OP.GG annonce une mise à jour horaire du ladder ; cela n’est pas une garantie de fraîcheur de chaque réponse MCP.

## v29 — cartes de rang et statistiques saisonnières

La cote personnelle utilise une carte avec grand emblème, palier/LP, serveur/Top % OP.GG, mondial indisponible et victoires/défaites Riot colorées. Flex est repliable. Les dates et limites de file sont conservées ; aucun rang mondial inventé.

Le footer de l’overlay est supprimé ; la provenance reste dans les infobulles. Dans le profil, Champions joués utilise directement les champions OP.GG de la saison et leurs images locales, victoires, KDA moyens et farm lorsque disponible. Le filtre des derniers matchs ne modifie pas ces données.

Bilan de saison utilise les totaux play/win/lose de most_champions (test réel : 692/353/339), pas la somme des dix champions renvoyés. KDA global et CS/min sont calculés uniquement si la liste couvre toutes les parties ; sinon tirets expliqués au survol. Participation globale et répartition saisonnière par rôle ne sont pas exposées dans le contrat public testé : état indisponible explicite, sans recyclage des derniers matchs. Les démonstrations gardent leurs données fictives.

Nouveau cache opgg-cache-v2 pour demander immédiatement les totaux et compteurs manquants du format précédent, TTL une heure inchangé. Test ajouté : une liste partielle ne permet pas de fabriquer des agrégats complets.
