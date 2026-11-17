# IA de Campus Tycoon — guide d'intégration

Utility AI : à chaque décision, l'IA élimine les actions illégales, applique ses contraintes de liquidité
(réserve d'argent), donne un score à chaque action restante et renvoie la meilleure, avec son score et une
justification courte (`AIDecision`). La personnalité du document de règles (chance d'achat, de construction,
de pouvoir) s'applique ensuite. L'IA ne modifie jamais l'état du jeu : la logique applique la décision.

## Fichiers

| Fichier | Rôle |
|---|---|
| `AIGameSnapshot.cs` | Vue de la partie en lecture seule (`SquareInfo`, `PlayerInfo`) |
| `AISnapshotBuilder.cs` | Pont : construit le snapshot à partir de `Player`, `BoardSquare` et `LevelData` |
| `BoardProbabilities.cs` | Probabilités d'arrivée avec 2d6, Rattrapage → colle, bonus de double |
| `RentEstimator.cs` | Loyers (R, 2R, niveaux 1 à 3, gares, Machine à café, événements) et revenus espérés |
| `UtilityScorer.cs` | Fonctions d'utilité : achat, amélioration, colle, billet, vente forcée, Roulette |
| `AIBrain.cs` | Décisions complètes (C# pur, testable hors Unity) |
| `AIParameters.cs` | Paramètres des 3 personnages (valeurs des règles) + modulation par difficulté |
| `AIProfile.cs` | ScriptableObject pour régler un profil dans l'Inspector |
| `AIController.cs` | MonoBehaviour appelé par la machine à états du tour |
| `AIBarks.cs` | Répliques des IA : textes par personnage et par moment, rythme (C# pur) |
| `AIBarkController.cs` | Écoute `CampusEvents` et envoie les répliques à la bulle du HUD |
| `Editor/AIProfileMenu.cs` | Menu **Campus Tycoon > IA > Créer les profils par défaut** (assets dans `Assets/Data/AI`) |

## Branchement dans la machine à états du tour

```csharp
// Début de partie, pour chaque rival (difficulté lue dans LevelData)
var ai = rivalGO.AddComponent<AIController>();
ai.Configure(player, level);          // profil optionnel en 3e argument, sinon valeurs des règles

// Avant chaque décision : photo de l'état réel (Player, BoardSquare, LevelData)
var snap = AISnapshotBuilder.Build(
    level,                 // LevelData du niveau en cours
    board,                 // List<BoardSquare>, dans l'ordre du plateau
    players,               // List<Player>, dans l'ordre de leur Index
    turnsPlayed,           // int[] : tours commencés par joueur, tour en cours compris
    activeEvent,           // GlobalEventType? : événement du semestre en cours, ou null
    upgradedThisTurn);     // cases déjà améliorées ce tour (facultatif)

var decision = ai.DecideBuy(snap, square);
if (decision.Action == AIActionType.Buy) { /* la logique applique l'achat */ }
```

Le GameManager doit tenir deux informations que le modèle ne porte pas encore :
le nombre de tours commencés par chaque joueur (les rejeux sur double ne comptent pas)
et l'événement du semestre actif. Tout le reste est lu dans `Player`, `BoardSquare` et `LevelData`.

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

## Répliques des IA

1. Ajouter `AIBarkController` sur un objet de la scène.
2. Dans l'Inspector, relier **On Speech** à `CampusHUDController.ShowAISpeech (string, float)`.
3. En début de partie : `barks.Bind(board, rivals)` (cases pour les noms et les monopoles, `AIController` des rivaux).

Moments couverts : achat, monopole, achat d'un rival, amélioration, loyer encaissé ou payé, colle, pouvoir,
faillite (la sienne ou celle d'un rival). Délai minimal de 3 s entre deux répliques ordinaires ; colle, monopole,
pouvoir et faillite passent toujours. Les textes sont dans `AIBarks.cs`.

Le `TurnManager` doit émettre `RaisePropertyBought` **après** avoir mis à jour le propriétaire de la case,
sinon le monopole n'est pas détecté.

## Choix de conception

- **Le prix n'est pas soustrait du score d'achat.** La fortune finale compte le prix des terrains et le coût
  des améliorations : acheter ne fait pas baisser la fortune. Le vrai coût est le risque de revendre à 50 %,
  mesuré par la pénalité de liquidité (dépenses espérées sur 2 tours + moitié du plus gros loyer adverse).
- **Probabilités réelles 2d6** au lieu de 1/N, sur l'horizon des tours restants de chaque adversaire.
- Les cartes qui déplacent le pion sont ignorées dans les probabilités (approximation).
- Techos « en colle » : interprété comme « le lancer l'enverrait sur Rattrapage ».
- Niveau 3 : le Techos remplace la règle « terrain rentable » par le score complet et garde sa réserve de 300
  (décision d'équipe du 03/11 : 35 % → 42 % de victoires en simulation contre deux rivaux en Normal).
- L'IA ne vend jamais volontairement (une vente fait perdre 50 % de fortune), seulement en cas de dette.

## Tests hors Unity

`Tools/AISimulation` compile ces scripts avec des stubs de UnityEngine, vérifie 73 scénarios (dont le pont avec le modèle) puis simule
des parties entre IA pour régler les coefficients :

```bash
cd Tools/AISimulation
dotnet run -c Release 500
```
