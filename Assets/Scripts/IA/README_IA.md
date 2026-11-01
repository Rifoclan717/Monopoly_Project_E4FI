# IA de Campus Tycoon — guide d'intégration

Utility AI : à chaque décision, l'IA élimine les actions illégales, applique ses contraintes de liquidité
(réserve d'argent), donne un score à chaque action restante et renvoie la meilleure, avec son score et une
justification courte (`AIDecision`). La personnalité du document de règles (chance d'achat, de construction,
de pouvoir) s'applique ensuite. L'IA ne modifie jamais l'état du jeu : la logique applique la décision.

## Fichiers

| Fichier | Rôle |
|---|---|
| `AIGameSnapshot.cs` | Vue de la partie que la logique remplit avant chaque décision (`SquareInfo`, `PlayerInfo`) |
| `BoardProbabilities.cs` | Probabilités d'arrivée avec 2d6, Rattrapage → colle, bonus de double |
| `RentEstimator.cs` | Loyers (R, 2R, niveaux 1 à 3, gares, Machine à café, événements) et revenus espérés |
| `UtilityScorer.cs` | Fonctions d'utilité : achat, amélioration, colle, billet, vente forcée, Roulette |
| `AIBrain.cs` | Décisions complètes (C# pur, testable hors Unity) |
| `AIParameters.cs` | Paramètres des 3 personnages (valeurs des règles) + modulation par difficulté |
| `AIProfile.cs` | ScriptableObject pour régler un profil dans l'Inspector |
| `AIController.cs` | MonoBehaviour appelé par la machine à états du tour |
| `Editor/AIProfileMenu.cs` | Menu **Campus Tycoon > IA > Créer les profils par défaut** (assets dans `Assets/Data/AI`) |

## Branchement dans la machine à états du tour

```csharp
// Début de partie, pour chaque rival
var ai = rivalGO.AddComponent<AIController>();
ai.Configure(index, CharacterId.Techie, levelData.Difficulty);   // profil optionnel en 4e argument

// Avant chaque décision : construire le snapshot depuis l'état réel
var snap = new AIGameSnapshot(board.Select(SquareInfo.From).ToList(), players);
for (int s = 0; s < n; s++) { snap.Owners[s] = ownerOf[s]; snap.Levels[s] = levelOf[s]; }
snap.ActiveEvent = currentGlobalEvent;   // null si aucun
snap.UpgradedThisTurn.UnionWith(upgradedThisTurn);
```

`PlayerInfo.TurnsRemaining` = tours qu'il reste à jouer à ce joueur, sans compter le tour en cours.

| État du tour | Appel | Action à appliquer |
|---|---|---|
| Début de tour en colle | `DecideDetention(snap)` | `WaitInDetention` / `PayDetentionFine` / `UseExemptionTicket` |
| Dés lancés (Techos) | `DecidePower(snap, new PowerContext { Moment = DiceRolled, FromPosition, DiceTotal })` | `UsePower` → relancer |
| Terrain libre | `DecideBuy(snap, square)` | `Buy` / `Pass` |
| Loyer encaissé (Major) | `DecidePower(snap, { Moment = CollectingRent, Amount = loyer })` | `UsePower` → loyer doublé |
| Loyer à payer | `ShouldUseTicketOnRent(snap, loyer)` puis, pour le Fêtard, `DecidePower(snap, { Moment = PayingRent, Amount })` | loyer annulé |
| Taxe (Fêtard) | `DecidePower(snap, { Moment = PayingTax, Amount })` | taxe annulée |
| Carte « Amélioration offerte » | `ChooseFreeUpgrade(snap)` | case, ou -1 → recevoir 60 |
| Roulette du BDE | `DecideRoulette(snap, square)` | `PlayRoulette` / `SkipRoulette` |
| Actions facultatives | `DecideUpgrades(snap)` | liste ordonnée de `Upgrade` |
| Dette > argent | `ChooseAssetsToSell(snap, montantManquant)` | liste ordonnée de `SellUpgrade` / `SellProperty` |

## Choix de conception

- **Le prix n'est pas soustrait du score d'achat.** La fortune finale compte le prix des terrains et le coût
  des améliorations : acheter ne fait pas baisser la fortune. Le vrai coût est le risque de revendre à 50 %,
  mesuré par la pénalité de liquidité (dépenses espérées sur 2 tours + moitié du plus gros loyer adverse).
- **Probabilités réelles 2d6** au lieu de 1/N, sur l'horizon des tours restants de chaque adversaire.
- Les cartes qui déplacent le pion sont ignorées dans les probabilités (approximation).
- Techos « en colle » : interprété comme « le lancer l'enverrait sur Rattrapage ».
- L'IA ne vend jamais volontairement (une vente fait perdre 50 % de fortune), seulement en cas de dette.

## Tests hors Unity

`Tools/AISimulation` compile ces scripts avec des stubs de UnityEngine, vérifie 38 scénarios puis simule
des parties entre IA pour régler les coefficients :

```bash
cd Tools/AISimulation
dotnet run -c Release 500
```
