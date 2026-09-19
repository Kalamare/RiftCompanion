# Rift Companion — prototype 01

## Application native Windows

Le projet utilise désormais **C# / .NET 10 LTS + WPF + SQLite** (décision du 16 septembre 2026). Le premier socle se trouve dans `native/` ; lancement avec **Lancer-Natif.cmd** après compilation. Voir [le guide natif](native/README.md) et [la décision d'architecture](docs/ADR-001-STACK-WINDOWS.md).

La suite de ce document décrit le prototype Node conservé comme référence.

Application locale de diagnostic pour League of Legends. Node.js 20 ou supérieur, sans paquet npm à installer. Interface française dans le navigateur. Ce prototype valide la connexion ; il ne fournit pas encore de recommandations, de statistiques de joueurs ou d'écart d'équipement.

## Démarrer sous Windows

1. Double-cliquer sur `Lancer.cmd` (ou lancer `npm start` dans ce dossier).
2. Ouvrir <http://127.0.0.1:3210> dans le navigateur.
3. Ouvrir LoL, puis une sélection de champions pour tester la synchronisation.
4. Arrêter le moteur avec Ctrl+C dans son terminal. Fermer la page ne l'arrête pas.

`Demo.cmd` ou `npm run demo` affiche une composition fictive sans accéder à LoL. Arrêter une instance avant de lancer l'autre : les deux utilisent le même port. Le mode démo est toujours signalé.

Si LoL est installé ailleurs, dans PowerShell :

```powershell
$env:LOL_DIRECTORY = 'D:\Jeux\League of Legends'
npm start
```

Chemins automatiquement essayés : `C:\Riot Games\League of Legends` et `D:\Riot Games\League of Legends`. La découverte lit le fichier `lockfile` dans ces dossiers, sans inspecter les lignes de commande des processus. Les installations différentes demandent `LOL_DIRECTORY`. Pour un autre port, définir `$env:PORT = '3211'` avant le lancement.

## Inclus

- État du client et phase de jeu, picks/pré-sélections, bans, rôle attribué et repère « toi ».
- Préférence parmi les cinq rôles, jungle par défaut, enregistrée uniquement dans le navigateur. Elle ne modifie pas le rôle attribué et ne déclenche pas encore de recommandations.
- Catalogue de noms lu depuis le client, au maximum deux essais par connexion. Identifiant affiché si le catalogue est indisponible.
- CPU du moteur rapporté à un cœur, mémoire résidente, nombre de tentatives LCU, durée de la dernière requête, erreurs de cycle et cadence récente.
- Mode démonstration isolé, reprise automatique après déconnexion, suppression de la draft à la sortie de sélection ou sur erreur.

## Charge et architecture

Le moteur Node expose une interface HTTP sur **127.0.0.1 uniquement**. La page reçoit les changements par SSE ; plusieurs pages ne multiplient pas les requêtes LoL. Les onglets cachés ferment leur flux, et aucune animation continue ne tourne. Le moteur reste actif pour suivre les changements de phase.

La boucle LCU est séquentielle : pause de 2 s après un cycle de draft (phase + session), 5 s au repos ou en erreur, 15 s en partie/reconnexion (phase seule). Un cycle peut prendre plus longtemps à cause des requêtes, bornées à 1,5 s chacune. Aucun sondage ne se chevauche. Pas d'accès à Internet pendant l'exécution, pas d'overlay, pas de capture vidéo. Une intégration événementielle LCU pourra remplacer le sondage après validation de sa stabilité sur le client installé.

Les métriques intégrées **excluent le navigateur et LoL**. Elles ne prouvent ni l'absence de lag ni un impact nul. La cadence est extrapolée sur cinq secondes, elle peut varier en raison de la fenêtre d'échantillonnage.

## Validation sur le PC

1. Mesurer LoL seul dans le Gestionnaire des tâches pendant une période stable (au moins 2 minutes) : CPU, mémoire, GPU du client et réactivité des mêmes écrans.
2. Répéter avec moteur + page visibles, puis page masquée. Inclure les processus du navigateur dans le coût total.
3. En partie d'entraînement, reproduire le même scénario et réglages ; comparer FPS et temps de rendu si l'outil de mesure le permet. Répéter pour distinguer fluctuations normales et régression.
4. Tester entrée/sortie de draft, changement de pick, bans, fermeture/réouverture de LoL et déconnexion réseau. Les adversaires peuvent avoir des rôles inconnus ; ne pas les inventer.
5. Noter les résultats dans `docs/validation.md`. Ne pas conclure sur les applications concurrentes sans mesures séparées.

## Développement et limites

`npm test` couvre parsing, filtrage d'identités, nettoyage d'état, reconnexion, cache et phases avec transport simulé. Les tests ne valident pas la compatibilité réelle avec une version de LoL. Le certificat auto-signé est accepté uniquement pour les requêtes HTTPS vers 127.0.0.1 et le port du lockfile. Les secrets restent en mémoire côté moteur ; aucune route ne les expose. Le serveur filtre Host/Origin et n'accepte que des lectures prédéfinies. Ne pas ajouter de proxy LCU générique.

Endpoints lus :

- `/lol-gameflow/v1/gameflow-phase`
- `/lol-champ-select/v1/session`
- `/lol-game-data/assets/v1/champion-summary.json`

Riot ne garantit pas la compatibilité de la LCU avec les applications tierces. Enregistrer le produit et déclarer ses endpoints auprès de Riot avant sa mise à disposition aux joueurs : [documentation officielle](https://developer.riotgames.com/docs/lol#league-client-api).

Prochaines étapes : validation réelle et mesures, recommandations explicables pour les cinq rôles, comparaison d'équipement visible (avec règles pour objets gratuits/améliorations), statistiques via API Riot quand les identités sont disponibles. Ne pas désanonymiser les joueurs ni exposer des informations cachées. [Règles Riot](https://support-developer.riotgames.com/hc/en-us/articles/22698698001939-League-of-Legends).
