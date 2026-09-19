# Validation du prototype

## État

- Client LoL non détecté au début de l'implémentation : connexion réelle et performances en partie à mesurer.
- Les tests automatisés utilisent un transport simulé. Ils ne remplacent pas une draft réelle.
- 15 septembre 2026 : 8 tests unitaires réussis (`npm test`). Syntaxe serveur et interface validée par Node.
- Vérifications HTTP : lecture de l'état 200, POST 405, Origin étranger 403, Host étranger 403, source serveur inaccessible 404.
- Mode démonstration : 10 champions affichés, bans présents, aucune requête LCU. Rendu contrôlé dans le navigateur ; préférence Support conservée après rechargement, puis rétablie à Jungle.
- Mode réel : un lockfile est présent mais la connexion renvoie ECONNREFUSED. L'interface signale la connexion indisponible et retente ; aucune draft réelle validée.
- Lecture ponctuelle en attente de connexion : moteur autour de 35–41 Mo de mémoire résidente. Ce n'est pas un benchmark et cela exclut le navigateur ; CPU instantané arrondi à 0,00 % insuffisant pour conclure à un impact nul.

## Relevés à compléter

| Scénario | CPU moteur | RAM moteur | CPU/RAM navigateur | CPU/RAM client LoL | Fluidité / FPS / temps de rendu |
| --- | --- | --- | --- | --- | --- |
| LoL seul | — | — | — | À mesurer | À mesurer |
| LoL + moteur + page | À mesurer | À mesurer | À mesurer | À mesurer | À mesurer |
| LoL + moteur + page masquée | À mesurer | À mesurer | À mesurer | À mesurer | À mesurer |

Conserver les mêmes conditions, durée, réglages graphiques et outils de mesure. Le mode démonstration ne mesure pas le coût de la connexion LCU.
