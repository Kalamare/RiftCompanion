# Backlog — Rift Companion

**Priorité P0 actuelle : fluidité du profil.** Données et images validées ; nouvelles fonctions différées. Corrections et mesures dans FLUIDITE-PROFIL.md. RC-17 passé P0 et À tester dans Notion.

**16 septembre 2026 :** priorité au profil individuel (RC-16/RC-17) avant la draft. Première version WPF à tester sur données Riot réelles ; 36 contrôles automatisés passent. Les statuts initiaux ci-dessous sont historiques ; le suivi actif est dans Notion. Voir PROFIL-JOUEUR.md.

Statut initial de toutes les cartes ci-dessous : À préciser. Le socle expérimental existant est décrit dans PLAN-PROJET.md ; ces cartes correspondent au travail restant. Priorité P0 = condition préalable, P1 = cœur de V1, P2 = après V1.

## J0 — Cadrage

### RC-01 · Choisir l'outil de suivi · P0

- Résultat : espace Notion ou tableau Trello, colonnes et propriétés définies dans le plan.
- Acceptation : chaque tâche a un ID, une priorité et un critère testable ; liens vers les documents versionnés.
- Dépendances : choix utilisateur et accès à l'espace. Aucun espace externe créé à ce stade.

### RC-02 · Définir les écrans et parcours V1 · P0

- Résultat : esquisses connexion, draft, équipement et joueurs ; états vide, chargement, incomplet et erreur.
- Acceptation : parcours sélection → partie → retour compréhensible ; rôle jungle par défaut et cinq rôles accessibles ; limites affichées.
- Dépendances : aucune.

## J1 — Données et architecture

### RC-03 · Mesurer le prototype et LoL seul · P0

- Résultat : rapport reproductible CPU/RAM/GPU/temps de rendu, visible et masqué.
- Acceptation : trois passages par scénario, matériel et conditions consignés ; navigateur inclus dans le coût.
- Dépendances : disponibilité de LoL pour les tests.

### RC-04 · Comparer les essais WPF et Tauri · P0

- Résultat : deux écrans de draft équivalents utilisant les mêmes données de test et rythmes de rafraîchissement.
- Acceptation : builds Release ; mesures de tous les processus ; complexité et effort de migration documentés.
- Dépendances : RC-02, RC-03 ; SDK nécessaires.

### RC-05 · Fixer la stack et les budgets · P0

- Résultat : décision technique versionnée ; versions maintenues ; budgets ajustés aux mesures.
- Acceptation : choix motivé et plan de reprise des fonctions existantes ; aucune promesse de zéro impact.
- Dépendances : RC-04.

### RC-06 · Vérifier données et conditions Riot · P0

- Résultat : inventaire endpoints, disponibilité, fraîcheur, quotas, clé adaptée et déclaration du produit.
- Acceptation : aucune identité cachée ; faisabilité du calcul d'équipement étudiée ; sources de matchups autorisées identifiées ou explicitement absentes.
- Dépendances : accès développeur selon besoin ; ne pas enregistrer de secrets dans les cartes.

## J2 — Socle desktop

### RC-07 · Construire l'application installable · P1

- Résultat : fenêtre Windows, lancement/arrêt propre, préférences et cache local.
- Acceptation : exécution depuis un chemin contenant des espaces ; relance ; absence de processus restant après arrêt explicite.
- Dépendances : RC-05.

### RC-08 · Fiabiliser la synchronisation LoL · P1

- Résultat : transitions, perte de connexion, reprise et découverte du dossier d'installation.
- Acceptation : événements évalués ; fallback borné ; pas d'abonnement dupliqué ni de draft périmée après déconnexion.
- Dépendances : RC-06, RC-07.

### RC-09 · Ajouter le diagnostic complet · P1

- Résultat : export local de mesures et erreurs expurgées.
- Acceptation : CPU total et CPU par cœur distingués ; tous les processus de l'application comptés ; aucun token ni identité privée exporté.
- Dépendances : RC-07.

## J3 — Draft

### RC-10 · Définir les profils de champions · P1

- Résultat : caractéristiques éditoriales versionnées et provenance documentée.
- Acceptation : rôles, dégâts, engage, contrôles, protection et frontline revus ; flex et inconnus autorisés ; couverture annoncée.
- Dépendances : RC-06.

### RC-11 · Gérer les préférences et le champion pool · P1

- Résultat : pool éditable par rôle, exclusions et préférences.
- Acceptation : sauvegarde/rechargement ; rôles distincts ; comportement explicite si le pool devient vide.
- Dépendances : RC-07.

### RC-12 · Implémenter les recommandations expliquées · P1

- Résultat : trois à cinq propositions, points forts/limites et confiance sur les données.
- Acceptation : picks/bans exclus ; cas de référence pour les cinq rôles ; changements de draft pris en compte ; aucun faux taux de victoire ; p95 mesuré.
- Dépendances : RC-08, RC-10, RC-11.

### RC-13 · Évaluer les suggestions avec l'utilisateur · P1

- Résultat : au moins 20 drafts de référence annotées, dont des cas incertains.
- Acceptation : jugements et désaccords consignés ; aucune règle modifiée seulement pour faire passer un unique exemple ; coefficients versionnés.
- Dépendances : RC-12.

## J4 — Équipement

### RC-14 · Valider la visibilité des objets · P0

- Résultat : tests contrôlés des achats adverses et données reçues ; convention documentée.
- Acceptation : comportement sous brouillard de guerre établi ; ne pas activer le live si la visibilité n'est pas démontrable.
- Dépendances : RC-06, disponibilité d'un scénario contrôlé.

### RC-15 · Calculer et présenter les écarts d'équipement · P1

- Résultat : valeur visible par joueur et équipe, fraîcheur et complétude.
- Acceptation : composants consommés non doublés ; objets gratuits/améliorés/stacks testés ; inventaire partiel signalé ; rôle incertain non imposé.
- Dépendances : RC-08, RC-14.

## J5 — Joueurs

### RC-16 · Intégrer les données Riot avec cache · P1

- Résultat : accès autorisé, cache par requête et gestion des quotas.
- Acceptation : erreurs 401/403/429, timeout et cache périmé traités ; secret hors binaire public et hors dépôt ; aucune boucle agressive de retry.
- Dépendances : RC-06, RC-07 ; clé adaptée.

### RC-17 · Afficher les fiches de joueurs · P1

- Résultat : statistiques avec période, mode, volume de parties et date de mise à jour.
- Acceptation : joueur caché respecté ; aucun historique inventé ; données absentes ou faibles clairement indiquées.
- Dépendances : RC-16.

## J6 — Validation

### RC-18 · Exécuter la campagne de performance et d'endurance · P0

- Résultat : rapport A/B visible/masqué avec LoL, endurance 60 minutes, reconnexions et transitions.
- Acceptation : budgets retenus respectés ou écarts corrigés/requalifiés explicitement ; pas de fuite mémoire progressive.
- Dépendances : RC-09, RC-13, RC-15, RC-17.

### RC-19 · Livrer la première version personnelle · P1

- Résultat : installation versionnée, guide de démarrage, liste des limites et procédure de retour à la version précédente.
- Acceptation : test après installation ; lancement/arrêt ; compatibilité avec le client courant ; critères V1 validés.
- Dépendances : RC-18.

## Après V1

### RC-20 · Étudier l'overlay facultatif · P2

- Résultat : essai et mesure séparés ; règles Riot vérifiées.
- Acceptation : désactivable ; aucun accès caché ; coût additionnel connu ; pas de déploiement automatique si le budget est dépassé.
- Dépendances : RC-19.

### RC-21 · Enrichir le modèle avec des statistiques de draft · P2

- Résultat : corpus autorisé, daté, versionné par patch/rôle/niveau ; pipeline de mise à jour.
- Acceptation : biais et taille d'échantillon traités ; validation sur données séparées ; coût/quotas connus ; comparaison contre V1.
- Dépendances : RC-06, RC-13.

## Mise en place dans un outil externe

Ce fichier est importable comme document Markdown dans Notion ; ce n'est pas automatiquement une base de tâches structurée. Pour Trello, créer les cartes à partir des titres puis y joindre critères et dépendances. Trello ne fournit pas d'import universel natif : utiliser son API/connecteur autorisé ou le collage de titres, puis vérifier les détails. Ne pas supposer qu'un CSV importe directement toutes les propriétés.

La création et le transfert dans l'espace choisi restent à effectuer une fois l'outil et l'accès disponibles.
