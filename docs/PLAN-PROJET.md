# Rift Companion — plan de projet

Mis à jour le 16 septembre 2026. Les comparaisons initiales sont conservées comme historique de décision.

**Décision du 16 septembre 2026 :** l'utilisateur retient C# / .NET 10 LTS + WPF + SQLite et lance la migration. L'essai comparatif Tauri n'est plus un préalable. Voir ADR-001-STACK-WINDOWS.md. Les comparaisons ci-dessous restent l'historique du choix ; les budgets de performance restent à vérifier.

**Priorité actuelle :** profil individuel avant la draft. Première version WPF implémentée : statistiques récentes, rangs, champions, rôles et historique. 36 contrôles passent ; données Riot authentifiées et impact sur LoL encore à valider. Détails : PROFIL-JOUEUR.md.

## 1. Objectif et périmètre

Créer une application Windows personnelle, discrète, pour aider à choisir un champion parmi plusieurs options compréhensibles, comparer l'équipement visible et consulter les statistiques accessibles des joueurs.

Public initial : un utilisateur, jungle par défaut, prise en charge des cinq rôles. Première version : fenêtre indépendante utilisable sur un second écran ou par Alt+Tab. Overlay facultatif après validation de son coût et des contraintes de Riot.

### État actuel

- Prototype Node + HTML/CSS/JavaScript sans dépendances npm ; connexion locale en lecture seule.
- États du client, draft, noms de champions, bans et rôles exposés par LoL.
- Huit tests automatisés et vérification visuelle. Connexion réelle détectée pendant une sélection d'entraînement ; l'utilisateur indique que cela semble fonctionner.
- Pas encore de campagne de performance, moteur de recommandation, comparaison d'équipement ou statistiques Riot.
- Compteurs CPU/RAM actuels limités au processus Node. Les résultats ne couvrent pas l'application complète.

## 2. Décision technique proposée

**Candidat principal : C# / .NET 10 LTS + WPF + SQLite.** Cible Windows assumée. Séparer logique métier, adaptateurs LoL et interface. Valider ce choix avec un petit essai mesuré avant de migrer le produit.

**Alternative à comparer : Tauri 2 + Rust + TypeScript + interface HTML/CSS actuelle + SQLite.** Réutilisation du rendu actuel ; coût WebView2 à inclure dans les mesures. Pas de moteur Node séparé embarqué par défaut.

Le prototype actuel sert de référence fonctionnelle et de référence de mesure. Le conserver jusqu'à parité des fonctions dans l'architecture retenue.

| Architecture complète | Avantages | Inconvénients | Avis pour le projet |
| --- | --- | --- | --- |
| Node + navigateur (actuel) | Développement rapide ; excellent pour échanges réseau asynchrones ; aucune migration immédiate | Deux éléments à lancer ; navigateur à compter ; packaging peu pratique ; calcul JS lourd susceptible de bloquer la boucle principale | Référence de test et outil de découverte LCU |
| Electron + TypeScript | Réemploi du web ; fenêtre, installation et écosystème desktop ; rendu Chromium fourni avec l'application | Chromium et Node embarqués ; plusieurs processus ; empreinte à surveiller ; mises à jour du runtime à distribuer | Possible, mais moins aligné avec notre priorité de sobriété |
| Tauri 2 + Rust + TypeScript | Interface web réutilisable ; moteur Rust ; WebView système, sans Chromium embarqué par l'application | Deux langages ; pont UI/moteur ; WebView2 consomme CPU/RAM ; petites distributions ne signifient pas petite RAM | Alternative solide, à mesurer |
| C# / .NET 10 + WPF | UI Windows sans moteur web ; asynchronisme et tâches en arrière-plan ; écosystème Windows mature | Réécriture de l'interface en XAML et du moteur ; Windows uniquement ; allocations et rendu peuvent aussi produire des pauses | Premier candidat pour notre cible Windows |

Ces appréciations sont des choix d'architecture, pas des résultats de benchmark. Aucune valeur de RAM théorique ni garantie de FPS n'est attribuée aux frameworks. Le langage seul ne décide pas de la fluidité : nombre de requêtes, allocations, rendu, tâches d'arrière-plan et overlay comptent aussi.

### Conditions de décision

- Comparer les trois variantes utiles (référence Node, essai WPF, essai Tauri) avec les mêmes données, fréquences, fonctionnalités et taille de fenêtre.
- Compiler les candidats en mode Release, sans débogueur ni rechargement de développement.
- Évaluer performance totale, effort de maintenance, fiabilité de connexion et facilité d'installation.
- Garder un tableau de résultats et écrire la décision finale avec les compromis. Ne pas migrer simplement parce qu'un langage est réputé rapide.
- Si le prototype Node reste utilisé, passer à une version LTS maintenue : Node 20 installé ici est désormais en fin de vie. Les SDK .NET 5/6 constatés ici ne constituent pas la cible ; installer le SDK .NET 10 si WPF est retenu.

## 3. Architecture cible

1. **Adaptateur client LoL** : découverte, authentification locale, phase et sélection ; reconnexion ; événements LCU si validés ; sondage borné en secours.
2. **Adaptateur partie** : données explicitement accessibles et pertinentes ; vérifier la visibilité des objets adverses avant d'activer leur comparaison.
3. **Domaine** : représentation normalisée de la draft, cinq rôles, champion pool, score explicable, comparaison d'équipement. Aucun appel HTTP dans le calcul de score.
4. **Données locales** : préférences et cache SQLite ; catalogue versionné par patch ; migrations de schéma ; bouton de réinitialisation du cache.
5. **Statistiques Riot** : quotas, cache, fraîcheur, erreurs et périodes d'analyse. Une clé de développement sert à l'expérimentation ; stratégie de clé adaptée à l'usage personnel puis à une distribution éventuelle.
6. **Interface** : connexion, draft, équipement et joueurs ; état vide/périmé/erreur explicite ; arrêt réel accessible et mode discret en partie.
7. **Diagnostic** : mesures de l'ensemble des processus, compteurs de requêtes, export local expurgé. Aucun secret ou identifiant privé dans les traces.

Ne pas ajouter de serveur cloud avant qu'une fonction l'exige. Pour une distribution publique, ne pas embarquer une clé Riot secrète dans le binaire : prévoir alors un service sécurisé avec quotas et budget d'exploitation.

## 4. Fonctionnalités et qualité attendue

### Draft

- Trois à cinq propositions, mises à jour avec picks et bans ; champions déjà choisis ou bannis exclus.
- Tous les rôles ; rôle inconnu ou adversaire potentiellement flex affiché comme incertain.
- Prise en compte du champion pool et des préférences ; ne pas assimiler peu de parties à une mauvaise maîtrise.
- V1 explicable : équilibre des dégâts, engage, frontline, protection, contrôles et portée, avec données éditoriales revues et versionnées.
- V2 statistique : matchups et synergies par rôle/patch/niveau, si une source exploitable et autorisée est disponible. Afficher l'échantillon et l'incertitude ; limiter les biais et petits échantillons.
- Score de pertinence, pas probabilité de victoire. Les coefficients initiaux sont des hypothèses à tester avec l'utilisateur.
- Jeux de drafts de référence : manque de frontline, dégâts homogènes, menace de dive, adversaire flex, données incomplètes, champion favori banni.

### Équipement

- Libellé « valeur d'équipement visible », jamais « or total ».
- Décider la convention : valeur du catalogue des objets possédés, plutôt que montant historiquement dépensé. Règles explicites pour objets gratuits, améliorés, stacks, consommables et objets sans prix exploitable.
- Ne jamais compter à la fois un objet terminé et ses composants déjà consommés.
- Vérifier en partie contrôlée que la donnée n'actualise pas des achats ennemis cachés ; si la visibilité ne peut être établie, ne pas publier un indicateur live qui prétend respecter cette visibilité.
- Valeurs absentes ou périmées signalées ; total d'équipe incomplet signalé ; comparaison par rôle seulement si le rôle est suffisamment établi.

### Joueurs

- Rang, résultats récents, expérience sur le champion, KDA et autres métriques retenues avec l'utilisateur.
- Fenêtre d'analyse et nombre de matchs visibles. Modes et rôles comparables ; données insuffisantes signalées.
- Respect de l'anonymat et du moment où les identités deviennent disponibles.
- Quotas Riot, clés expirées et mode hors ligne traités sans bloquer l'interface.

## 5. Performance : protocole et budgets proposés

Ces seuils sont des objectifs initiaux à confirmer sur le PC, pas des résultats acquis ni des garanties universelles.

| Mesure | Objectif proposé | Méthode |
| --- | --- | --- |
| CPU total de l'application, masquée en partie | Moyenne ≤ 0,5 % de la capacité CPU totale sur 5 min | Additionner tous les processus attribuables ; documenter nombre de processeurs logiques |
| Mémoire résidente totale | ≤ 150 Mo masquée ; ≤ 250 Mo visible | Inclure runtime, moteur et WebView éventuelle ; mesure après stabilisation ; comparer aussi mémoire privée |
| Mise à jour d'un pick | p95 ≤ 500 ms si événements LCU fiables ; sinon ≤ 3 s avec sondage | Depuis l'événement reçu ou un changement observable jusqu'au rendu ; indiquer exactement le point de départ |
| Calcul local de recommandations | p95 ≤ 100 ms sur jeu de données de référence | Cache chaud, hors réseau, travail hors thread UI |
| Impact jeu | Pas de dégradation reproductible supérieure à 3 % du temps de rendu p95 | Comparaison A/B répétée ; seuil expérimental, tenir compte du bruit |
| Stabilité | Pas de croissance mémoire continue après échauffement sur 60 min | Scénario rejouable avec connexions, drafts et retours accueil |

Scénarios : LoL seul, prototype actif visible, application masquée, retour au premier plan, cache froid/chaud, déconnexion/reconnexion, indisponibilité API. Trois passages minimum par scénario ; alterner l'ordre pour réduire le biais thermique. Relever matériel, Windows, patch LoL, réglages, résolution, limite FPS et outils. Distinguer lenteur du client, FPS et latence réseau.

Les requêtes événementielles ne sont pas une promesse : vérifier leur disponibilité et leur comportement après reconnexion. Un abonnement ne doit jamais provoquer un nombre croissant de traitements ou une tempête de rafraîchissements.

## 6. Jalons

Les efforts sont des ordres de grandeur de travail actif, pas des dates de livraison. Revoir l'estimation après J1 ; les accès Riot et les tests manuels peuvent allonger le calendrier.

| Jalon | Livrable | Condition de sortie | Effort indicatif |
| --- | --- | --- | --- |
| J0 — Cadrage | Écrans prioritaires, tableau de tâches, critères de performance | Périmètre V1 et cas d'usage fixés ; responsabilités attribuées | 0,5–1 jour |
| J1 — Choix technique | Référence mesurée, petits essais WPF/Tauri, décision écrite | Comparaison équivalente ; runtime maintenu choisi ; accès aux données étudié | 2–4 jours |
| J2 — Socle desktop | Installation locale, reconnexion, préférences, diagnostic | Fonctions actuelles reproduites ; fermeture propre ; tests de transition | 3–5 jours |
| J3 — Draft V1 | Suggestions expliquées pour cinq rôles et champion pool | Cas de référence validés ; aucune recommandation bannie ; latence mesurée | 4–7 jours |
| J4 — Équipement | Comparaisons individuelles et équipes | Règles de valeur et visibilité validées ; cas limites couverts | 2–4 jours |
| J5 — Statistiques | Fiches joueurs et cache Riot | Identités disponibles uniquement ; quotas/erreurs testés ; fraîcheur visible | 3–6 jours |
| J6 — Validation personnelle | Version installable et rapport de performance | Scénarios réels, endurance, absence de régression au-delà des budgets retenus | 2–4 jours |

Total initial : environ 17–31 jours de travail actif, hors constitution d'un gros corpus statistique, attente d'accès externes, overlay et publication publique. Une estimation plus précise serait prématurée.

## 7. Organisation du travail

**Outil recommandé : Notion**, pour réunir cahier des charges, décisions et tableau des tâches. **Trello** convient si l'objectif principal est de déplacer simplement des cartes. Conserver les décisions techniques et critères de validation dans le dépôt pour les versionner, quel que soit l'outil choisi.

Un seul tableau de référence : Idées → À préciser → Prêt → En cours → À tester → Terminé. Blocage représenté par une propriété et sa raison. Une seule grosse tâche en cours à la fois ; terminer et vérifier avant d'empiler les fonctions.

Propriétés : ID, titre, module, priorité P0/P1/P2, jalon, statut, dépendances, responsable, effort réestimé, critères d'acceptation, preuve de test. Pas de date inventée avant attribution et estimation.

Rituel à chaque étape : choisir une petite tâche prête, implémenter, tester, noter le résultat, déplacer la carte. À chaque jalon : démo avec l'utilisateur, revue des performances, révision du backlog.

Définition de « Terminé » : comportement conforme aux critères ; tests utiles passés ; erreurs traitées ; budget vérifié si impact possible ; documentation mise à jour ; preuve ou limite restante attachée. Une simple implémentation non vérifiée reste « À tester ».

## 8. Risques principaux

| Risque | Réponse prévue |
| --- | --- |
| Changement de l'API locale non supportée | Adaptateur isolé, captures de test expurgées, mode dégradé et reconnexion |
| Peu de données statistiques pertinentes | Démarrer avec critères éditoriaux explicites ; ne pas simuler de vrais winrates |
| Information d'équipement non visible | Validation spécifique avant activation ; désactiver le cas non démontré |
| Lag dû au rendu ou aux requêtes | Mesurer tous les processus, cache, travail asynchrone, fréquences bornées |
| Clé Riot et quotas | Clés hors dépôt, cache, gestion des erreurs, stratégie de distribution séparée |
| Projet trop large | V1 fenêtre indépendante ; overlay, IA générative et fonctions sociales hors périmètre |

## 9. Sources officielles consultées

- [Node : fonctionnement de la boucle d'événements](https://nodejs.org/en/learn/asynchronous-work/dont-block-the-event-loop)
- [Node : versions maintenues](https://nodejs.org/en/about/previous-releases)
- [Electron : performance et processus](https://www.electronjs.org/docs/latest/tutorial/performance)
- [Tauri : processus Rust et WebView](https://tauri.app/concept/process-model/)
- [Tauri : WebView système](https://v2.tauri.app/reference/webview-versions/)
- [Microsoft : WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)
- [.NET : politique de support](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [Notion : import Markdown et données](https://www.notion.com/help/import-data-into-notion)
- [Trello : options d'import](https://support.atlassian.com/trello/docs/importing-data-into-trello/)
- [Riot : API locale et données](https://developer.riotgames.com/docs/lol)

Les sources décrivent les technologies et leurs contraintes ; elles ne constituent pas un benchmark de notre application.
