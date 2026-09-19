export const demoChampions = { 516: 'Ornn', 64: 'Lee Sin', 103: 'Ahri', 222: 'Jinx', 412: 'Thresh', 58: 'Renekton', 234: 'Viego', 112: 'Viktor', 145: 'Kai’Sa', 89: 'Leona', 35: 'Shaco', 238: 'Zed', 53: 'Blitzcrank', 157: 'Yasuo' };
export function demoState() {
  const roles = ['top', 'jungle', 'middle', 'bottom', 'utility'];
  const rows = (ids, allies) => ids.map((id, i) => ({ cellId: allies ? i : i + 5, championId: id, intentId: 0, role: roles[i], isYou: allies && i === 1 }));
  return { connected: false, phase: 'ChampSelect', message: 'Données fictives · aucune connexion à LoL',
    draft: { allies: rows([516, 64, 103, 222, 412], true), enemies: rows([58, 234, 112, 145, 89], false),
      bans: { allies: [35, 238], enemies: [53, 157] }, timerPhase: 'FINALIZATION' } };
}
