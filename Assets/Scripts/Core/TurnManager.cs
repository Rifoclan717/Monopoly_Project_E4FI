using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using CampusTycoon.AI;

namespace CampusTycoon
{
    /// <summary>
    /// Machine à états du tour : Lancer, Déplacer, Résoudre, Actions, Fin.
    /// Seule classe qui modifie l'état de la partie. La vue suit par CampusEvents,
    /// l'IA décide à partir d'un snapshot et ne touche jamais à l'état.
    /// </summary>
    public class TurnManager : MonoBehaviour
    {
        public enum TurnPhase { Idle, Detention, Roll, Move, Resolve, Actions, End, GameOver }

        [Header("Partie")]
        [SerializeField] private LevelData level;
        [Tooltip("Index 0 = humain, 1 et 2 = IA.")]
        [SerializeField] private CharacterData[] characters = new CharacterData[3];

        [Header("Vue (trouvée automatiquement si vide, facultative)")]
        [SerializeField] private ProceduralBoardGenerator board;
        [Tooltip("Vide = les 3 pions créés par le plateau (GetStudentPawn).")]
        [SerializeField] private PawnSplineMover[] pawns = new PawnSplineMover[0];
        [SerializeField] private ProceduralDiceRoller dice;
        [SerializeField] private CampusHUDController hud;

        [Header("Rythme")]
        [Min(0f)] [SerializeField] private float aiPause = 0.6f;
        [Min(1f)] [SerializeField] private float humanDecisionSeconds = 10f;

        public GameState State { get; private set; }
        public TurnPhase Phase { get; private set; } = TurnPhase.Idle;

        private readonly AIController[] ai = new AIController[3];
        private readonly List<GlobalEventData> eventPool = new List<GlobalEventData>();
        private GlobalEventData activeEvent;
        private int activeEventRoundsLeft;
        private int lastDiceTotal;
        private readonly List<CardData> deck = new List<CardData>();

        private void Start()
        {
            if (level == null || !level.Validate()) { Debug.LogError("[TurnManager] Aucun niveau valide assigné."); return; }
            if (characters.Length != 3 || characters.Any(c => c == null)) { Debug.LogError("[TurnManager] Il faut 3 personnages."); return; }

            if (board == null) board = FindFirstObjectByType<ProceduralBoardGenerator>();
            if (dice == null) dice = FindFirstObjectByType<ProceduralDiceRoller>();
            if (hud == null) hud = FindFirstObjectByType<CampusHUDController>();

            State = new GameState(level, characters);
            eventPool.AddRange(level.GlobalEvents);
            for (int i = 1; i < State.Players.Count; i++)
            {
                Player rival = State.Players[i];
                ai[i] = gameObject.AddComponent<AIController>();
                ai[i].Configure(rival, level, rival.Character.AIProfile);
            }
            StartCoroutine(GameLoop());
        }

        // Boucle de partie

        private IEnumerator GameLoop()
        {
            yield return null; // le plateau 3D se génère et crée les 3 pions dans son propre Start
            if (board != null && (pawns == null || pawns.Length == 0))
                pawns = new[] { board.GetStudentPawn(0), board.GetStudentPawn(1), board.GetStudentPawn(2) };
            if (board != null && board.levelData != level)
                Debug.LogWarning("[TurnManager] Le plateau 3D n'a pas le même LevelData : glissez le même niveau dans son champ Level Data.");
            if (board != null && board.generatedTiles.Count != State.SquareCount)
                Debug.LogWarning($"[TurnManager] Plateau 3D : {board.generatedTiles.Count} cases, niveau : {State.SquareCount}. Vérifier le Campus Level du plateau.");
            foreach (PawnSplineMover pawn in pawns)
                if (pawn != null && board != null) pawn.SnapToTile(board, 0);

            while (!IsGameOver())
            {
                for (int p = 0; p < State.Players.Count && !IsGameOver(); p++)
                {
                    if (State.Players[p].IsBankrupt) continue;
                    State.CurrentPlayerIndex = p;
                    yield return PlayTurn(State.Players[p]);
                }
                if (IsGameOver()) break;
                EndOfRound();
                State.Round++;
            }
            EndGame();
        }

        private bool IsGameOver() =>
            State.Round > level.MaxTurns || State.Players[0].IsBankrupt || State.ActivePlayers.Count() <= 1;

        private IEnumerator PlayTurn(Player player)
        {
            State.UpgradedThisTurn.Clear();
            State.TurnsStarted[player.Index]++; // un tour passé ou en colle compte aussi
            CampusEvents.RaiseTurnStarted(player.Index, State.Round, level.MaxTurns);

            if (player.MustSkipNextTurn)
            {
                player.SetSkipNextTurn(false);
                CampusEvents.RaiseTurnSkipped(player.Index);
                Debug.Log($"[Tour] {player.Character.DisplayName} passe son tour.");
                yield return Pause(player);
                yield break;
            }

            if (player.InDetention)
            {
                Phase = TurnPhase.Detention;
                bool skipTurn = false;
                yield return HandleDetention(player, skip => skipTurn = skip);
                if (skipTurn) { yield return Pause(player); yield break; }
            }

            CampusEvents.RaiseDoubleRolled(player.Index);
            bool firstRoll = true;
            while (true)
            {
                Phase = TurnPhase.Roll;
                if (player.IsHuman) yield return WaitForSpace("Espace : lancer les dés");
                int d1 = Random.Range(1, 7);
                int d2 = Random.Range(1, 7);
                
                yield return AnimateDice(d1, d2);
                CampusEvents.RaiseDiceRolled(player.Index, d1, d2);
                
                lastDiceTotal = d1 + d2;

                // Techos, relance, après le lancer et avant le déplacement
                bool rerolled = false;
                yield return OfferPower(player, PowerType.Reroll,
                    new PowerContext { Moment = PowerMoment.DiceRolled, FromPosition = player.Position, DiceTotal = lastDiceTotal },
                    $"Relance : relancer les dés (vous avez fait {lastDiceTotal}) ? O = oui, N = non", r => rerolled = r);
                if (rerolled)
                {
                    d1 = Random.Range(1, 7);
                    d2 = Random.Range(1, 7);
                    yield return AnimateDice(d1, d2);
                    CampusEvents.RaiseDiceRolled(player.Index, d1, d2);
                    lastDiceTotal = d1 + d2;
                }

                Phase = TurnPhase.Move;
                yield return MoveBy(player, lastDiceTotal);

                Phase = TurnPhase.Resolve;
                yield return Resolve(player);

                bool replay = d1 == d2 && firstRoll && !player.InDetention && !player.IsBankrupt && !IsGameOver();
                if (!replay) break;
                firstRoll = false;
                Debug.Log($"[Tour] Double : {player.Character.DisplayName} rejoue une fois.");
            }

            Phase = TurnPhase.Actions;
            if (!player.IsBankrupt && !IsGameOver()) yield return DoUpgrades(player);

            Phase = TurnPhase.End;
            yield return Pause(player);
        }

        // Événements du semestre 

        /// <summary>Fin d'un tour de table </summary>
        private void EndOfRound()
        {
            if (activeEvent != null && --activeEventRoundsLeft <= 0)
            {
                if (activeEvent.Type == GlobalEventType.CareerFair)
                    foreach (Player p in State.ActivePlayers)
                        p.AddMoney(activeEvent.Amount * State.CountOwned(p.Index, SquareType.Property));
                Debug.Log($"[Semestre] Fin de : {activeEvent.Title}");
                CampusEvents.RaiseGlobalEventEnded(activeEvent.Title);
                activeEvent = null;
                State.ActiveEvent = null;
            }

            if (activeEvent == null && eventPool.Count > 0 && level.GlobalEventTurns.Contains(State.Round))
            {
                activeEvent = eventPool[Random.Range(0, eventPool.Count)];
                eventPool.Remove(activeEvent); // sans répétition dans la même partie
                activeEventRoundsLeft = activeEvent.DurationRounds;
                State.ActiveEvent = activeEvent.Type;
                Debug.Log($"[Semestre] {activeEvent.Title} : {activeEvent.Description}");
                CampusEvents.RaiseGlobalEventStarted(activeEvent.Title, activeEvent.Description);
            }
        }

        // Résolution de la case d'arrivée 

        private IEnumerator Resolve(Player player)
        {
            BoardSquare square = State.Board[player.Position];
            switch (square.Data.Type)
            {
                case SquareType.Property:
                case SquareType.Station:
                case SquareType.Utility:
                    if (!square.IsOwned) yield return OfferPurchase(player, square);
                    else if (square.OwnerIndex != player.Index) yield return PayRent(player, square);
                    break;
                case SquareType.Tax:
                    yield return PayTax(player, square.Data.Amount);
                    break;
                case SquareType.StudentLounge:
                    player.AddMoney(square.Data.Amount);
                    CampusEvents.RaiseLoungeBonus(player.Index, square.Data.Amount);
                    break;
                case SquareType.GoToDetention:
                    yield return SendToDetention(player);
                    break;
                case SquareType.EventCard:
                    yield return DrawAndApplyCard(player);
                    break;
                
                case SquareType.Roulette:
                    yield return PlayRoulette(player, square);
                    break;
            }
        }

        private IEnumerator OfferPurchase(Player player, BoardSquare square)
        {
            int price = square.Data.Price;
            if (!player.CanAfford(price)) yield break;

            bool buy = false;
            if (player.IsHuman)
            {
                yield return AskYesNo($"Acheter {square.Data.DisplayName} pour {price} ? O = oui, N = non", answer => buy = answer);
            }
            else
            {
                AIDecision decision = ai[player.Index].DecideBuy(Snapshot(), square.Index);
                buy = decision.Action == AIActionType.Buy;
                yield return new WaitForSeconds(aiPause);
            }
            if (!buy) yield break;

            player.RemoveMoney(price);
            square.SetOwner(player.Index);
            CampusEvents.RaisePropertyBought(player.Index, square.Index, price);
            RefreshVisual(square);
        }

        private CardData DrawCard()
        {
            if (deck.Count == 0) deck.AddRange(level.CardDeck); // paquet épuisé : on le reconstitue
            int i = Random.Range(0, deck.Count);
            CardData card = deck[i];
            deck.RemoveAt(i);
            return card;
        }

        private IEnumerator DrawAndApplyCard(Player player)
        {
            CardData card = DrawCard();
            int cardId = level.CardDeck.ToList().IndexOf(card);
            Debug.Log($"[Carte] {player.Character.DisplayName} : {card.Title} — {card.Description}");
            CampusEvents.RaiseCardDrawn(player.Index, cardId, card.Title, card.Description);
            yield return new WaitForSeconds(player.IsHuman ? 1.5f : aiPause);

            switch (card.Effect)
            {
                case CardEffect.GainMoney:
                    player.AddMoney(card.Amount);
                    break;

                case CardEffect.PayMoney:
                    Pay(player, card.Amount, GameState.BankIndex);
                    break;

                case CardEffect.MoveSteps:
                    yield return MoveBy(player, card.Amount);
                    yield return Resolve(player);
                    break;

                case CardEffect.MoveToStart:
                    // Avance jusqu'à la case 0 : passer par là donne le bonus (150).
                    yield return MoveBy(player, State.SquareCount - player.Position);
                    break;

                case CardEffect.GoToDetention:
                    yield return SendToDetention(player);
                    break;

                case CardEffect.CollectFromEachRival:
                    foreach (Player rival in State.ActivePlayers.ToList())
                        if (rival.Index != player.Index) Pay(rival, card.Amount, player.Index);
                    break;

                case CardEffect.SkipNextTurn:
                    player.SetSkipNextTurn(true);
                    break;

                case CardEffect.RerollAndMove:
                    int d1 = Random.Range(1, 7);
                    int d2 = Random.Range(1, 7);
                    yield return AnimateDice(d1, d2);
                    CampusEvents.RaiseDiceRolled(player.Index, d1, d2);
                    lastDiceTotal = d1 + d2;
                    yield return MoveBy(player, lastDiceTotal);
                    yield return Resolve(player);
                    break;

                case CardEffect.FreeUpgrade:
                    ApplyFreeUpgrade(player, card.Amount);
                    break;

                case CardEffect.ExemptionTicket:
                    player.GiveExemptionTicket();
                    break;
            }
        }

        /// <summary>Création étage gratuit sur un terrain en monopole, sinon le joueur reçoit fallbackMoney.</summary>
        private void ApplyFreeUpgrade(Player player, int fallbackMoney)
        {
            List<BoardSquare> candidates = State.OwnedBy(player.Index)
                .Where(s => s.CanBeUpgraded && State.HasMonopoly(player.Index, s.Data.Group)).ToList();

            BoardSquare target = null;
            if (candidates.Count > 0)
            {
                if (player.IsHuman)
                    target = candidates.OrderByDescending(s => s.Data.UpgradeCost).First();
                else
                {
                    int chosen = ai[player.Index].ChooseFreeUpgrade(Snapshot());
                    target = candidates.FirstOrDefault(s => s.Index == chosen) ?? candidates[0];
                }
            }

            if (target == null) { player.AddMoney(fallbackMoney); return; }
            target.Upgrade();
            CampusEvents.RaisePropertyUpgraded(player.Index, target.Index, target.UpgradeLevel);
            RefreshVisual(target);
        }

        private static readonly int[] roulettePayouts = { 0, 0, 100, 100, 200, 200 };

        private IEnumerator PlayRoulette(Player player, BoardSquare square)
        {
            int stake = square.Data.Amount;
            if (!player.CanAfford(stake)) yield break;

            bool play = false;
            if (player.IsHuman)
            {
                yield return AskYesNo($"Roulette du BDE : miser {stake} ? O = oui, N = non", answer => play = answer);
            }
            else
            {
                AIDecision decision = ai[player.Index].DecideRoulette(Snapshot(), square.Index);
                play = decision.Action == AIActionType.PlayRoulette;
                yield return new WaitForSeconds(aiPause);
            }
            if (!play) yield break;

            player.RemoveMoney(stake);
            int die = Random.Range(1, 7);
            if (dice != null)
            {
                bool rolled = false;
                dice.RollDice(die, 1, (a, b) => rolled = true, true); 
                yield return WaitWithTimeout(() => rolled, 5f);
            }
            int payout = roulettePayouts[die - 1];
            player.AddMoney(payout);
            CampusEvents.RaiseRouletteResolved(player.Index, die, payout);
            Debug.Log($"[Roulette] {player.Character.DisplayName} mise {stake}, dé : {die}, gain {payout} (net {payout - stake:+0;-0;0}).");
        }

        private IEnumerator OfferPower(Player player, PowerType expected, PowerContext context,
            string humanPrompt, System.Action<bool> result)
        {
            result(false);
            if (!player.CanUsePower || player.Character.Power != expected) yield break;

            bool use = false;
            if (player.IsHuman)
            {
                yield return AskYesNo(humanPrompt, answer => use = answer);
            }
            else
            {
                use = ai[player.Index].DecidePower(Snapshot(), context).Action == AIActionType.UsePower;
                yield return new WaitForSeconds(aiPause);
            }
            if (!use) yield break;

            player.UsePower(); // lève OnPowerUsed : le HUD affiche le pouvoir
            result(true);
        }

        private IEnumerator PayTax(Player player, int amount)
        {
            bool used = false;
            yield return OfferPower(player, PowerType.CancelPayment,
                new PowerContext { Moment = PowerMoment.PayingTax, Amount = amount },
                $"Soirée mémorable : annuler la taxe de {amount} ? O = oui, N = non", r => used = r);
            if (used) yield break;
            Pay(player, amount, GameState.BankIndex);
        }

        private IEnumerator PayRent(Player tenant, BoardSquare square)
        {
            int rent = RentCalculator.Rent(State, square, lastDiceTotal);
            if (rent <= 0) yield break;
            Player owner = State.Players[square.OwnerIndex];

            // 1. Major : Dossier béton double le loyer.
            bool used = false;
            yield return OfferPower(owner, PowerType.DoubleRent,
                new PowerContext { Moment = PowerMoment.CollectingRent, Amount = rent },
                $"Dossier béton : doubler le loyer de {rent} ? O = oui, N = non", r => used = r);
            if (used) rent *= 2;

            // Absence du locataire : annule le loyer.
            if (tenant.HasExemptionTicket)
            {
                bool useTicket = false;
                if (tenant.IsHuman)
                    yield return AskYesNo($"Billet d'absence : annuler le loyer de {rent} ? O = oui, N = non", a => useTicket = a);
                else
                    useTicket = ai[tenant.Index].ShouldUseTicketOnRent(Snapshot(), rent);

                if (useTicket && tenant.ConsumeExemptionTicket())
                {
                    Debug.Log($"[Loyer] {tenant.Character.DisplayName} annule {rent} avec un billet d'absence.");
                    yield break;
                }
            }

            // Fêtard : Soirée mémorable annule le loyer
            used = false;
            yield return OfferPower(tenant, PowerType.CancelPayment,
                new PowerContext { Moment = PowerMoment.PayingRent, Amount = rent },
                $"Soirée mémorable : annuler le loyer de {rent} ? O = oui, N = non", r => used = r);
            if (used) yield break;

            Pay(tenant, rent, owner.Index);
            CampusEvents.RaiseRentPaid(tenant.Index, owner.Index, square.Index, rent);
            Debug.Log($"[Loyer] {tenant.Character.DisplayName} paie {rent} à {owner.Character.DisplayName} ({square.Data.DisplayName}).");
        }

        //Argent, ventes et faillite

        /// <summary>creditor = index du joueur payé, ou GameState.BankIndex. Vend des biens si l'argent manque.</summary>
        private void Pay(Player payer, int amount, int creditor)
        {
            if (amount <= 0) return;
            if (!payer.CanAfford(amount)) RaiseCash(payer, amount);
            if (!payer.CanAfford(amount)) { Bankrupt(payer, creditor); return; }

            payer.RemoveMoney(amount);
            if (creditor != GameState.BankIndex) State.Players[creditor].AddMoney(amount);
        }

        /// <summary>
        /// Vend des biens à 50 % jusqu'à pouvoir payer amountDue. IA : son plan (ChooseAssetsToSell).
        /// Humain : vente automatique de ce qui vaut le moins, en attendant un écran de vente.
        /// </summary>
        private void RaiseCash(Player player, int amountDue)
        {
            if (!player.IsHuman)
            {
                int missing = amountDue - player.Money;
                foreach (AIDecision d in ai[player.Index].ChooseAssetsToSell(Snapshot(), missing))
                {
                    if (player.CanAfford(amountDue)) return;
                    BoardSquare s = State.Board[d.Square];
                    if (s.OwnerIndex != player.Index) continue;
                    if (d.Action == AIActionType.SellUpgrade && s.UpgradeLevel > 0) SellUpgrade(player, s);
                    else if (d.Action == AIActionType.SellProperty && s.UpgradeLevel == 0) SellProperty(player, s);
                }
            }

            // Humain, ou plan de l'IA insuffisant : améliorations d'abord, puis terrains, du moins cher au plus cher.
            while (!player.CanAfford(amountDue))
            {
                BoardSquare upgrade = State.OwnedBy(player.Index).Where(s => s.UpgradeLevel > 0)
                    .OrderBy(s => s.Data.UpgradeCost).FirstOrDefault();
                if (upgrade != null) { SellUpgrade(player, upgrade); continue; }

                BoardSquare property = State.OwnedBy(player.Index).OrderBy(s => s.Data.Price).FirstOrDefault();
                if (property == null) return; // plus rien à vendre
                SellProperty(player, property);
            }
        }

        private int UpgradeCostOf(BoardSquare square) =>
            State.ActiveEvent == GlobalEventType.BackToSchoolSale ? square.Data.UpgradeCost / 2 : square.Data.UpgradeCost;

        private bool CanUpgrade(Player player, BoardSquare square) =>
            square.OwnerIndex == player.Index
            && square.CanBeUpgraded
            && State.HasMonopoly(player.Index, square.Data.Group)
            && !State.UpgradedThisTurn.Contains(square.Index)
            && player.CanAfford(UpgradeCostOf(square));

        private IEnumerator DoUpgrades(Player player)
        {
            if (player.IsHuman)
            {
                foreach (BoardSquare square in State.OwnedBy(player.Index).ToList())
                {
                    if (!CanUpgrade(player, square)) continue;
                    bool yes = false;
                    yield return AskYesNo(
                        $"Améliorer {square.Data.DisplayName} (niveau {square.UpgradeLevel} → {square.UpgradeLevel + 1}) pour {UpgradeCostOf(square)} ? O = oui, N = non",
                        answer => yes = answer);
                    if (yes) ApplyUpgrade(player, square);
                }
                yield break;
            }

            foreach (AIDecision decision in ai[player.Index].DecideUpgrades(Snapshot()))
            {
                if (decision.Action != AIActionType.Upgrade) continue;
                BoardSquare square = State.Board[decision.Square];
                if (!CanUpgrade(player, square)) continue;
                ApplyUpgrade(player, square);
                yield return new WaitForSeconds(aiPause);
            }
        }

        private void ApplyUpgrade(Player player, BoardSquare square)
        {
            player.RemoveMoney(UpgradeCostOf(square));
            square.Upgrade();
            State.UpgradedThisTurn.Add(square.Index);
            CampusEvents.RaisePropertyUpgraded(player.Index, square.Index, square.UpgradeLevel);
            RefreshVisual(square);
            Debug.Log($"[Amélioration] {player.Character.DisplayName} : {square.Data.DisplayName} niveau {square.UpgradeLevel}.");
        }

        private void SellUpgrade(Player player, BoardSquare square)
        {
            square.Downgrade();
            player.AddMoney(square.Data.UpgradeCost / 2);
            CampusEvents.RaisePropertyDowngraded(player.Index, square.Index, square.UpgradeLevel);
            RefreshVisual(square);
        }

        private void SellProperty(Player player, BoardSquare square)
        {
            square.ClearOwner();
            player.AddMoney(square.Data.Price / 2);
            CampusEvents.RaisePropertySold(player.Index, square.Index, square.Data.Price / 2);
            RefreshVisual(square);
        }

        private void Bankrupt(Player player, int creditor)
        {
            int rest = player.Money;
            if (rest > 0)
            {
                player.RemoveMoney(rest);
                if (creditor != GameState.BankIndex) State.Players[creditor].AddMoney(rest);
            }
            // Règle : les terrains retournent à la banque, libres et sans amélioration.
            // Le plateau est remis à zéro AVANT l'événement de faillite.
            foreach (BoardSquare s in State.OwnedBy(player.Index).ToList())
            {
                s.ClearOwner();
                RefreshVisual(s);
            }
            player.DeclareBankrupt(creditor);
        }

        /// <summary>Met le bâtiment à jour directement (entre autres pour les ventes, qui n'ont pas d'événements).</summary>
        private void RefreshVisual(BoardSquare square)
        {
            if (board != null) board.UpdatePropertyVisual(square.Index, square.OwnerIndex, square.UpgradeLevel);
        }

        // Colle

        private IEnumerator HandleDetention(Player player, System.Action<bool> setSkipTurn)
        {
            int fine = level.DetentionFine;
            AIActionType choice = AIActionType.WaitInDetention;

            if (player.IsHuman)
            {
                string prompt = "Colle :";
                if (player.CanAfford(fine)) prompt += $" P = payer {fine},";
                if (player.HasExemptionTicket) prompt += " B = billet d'absence,";
                prompt += " T = attendre un tour";

                Debug.Log($"[Tour] {prompt}");
                if (hud != null) hud.StartHumanDecisionTimer(prompt);
                float timeLeft = humanDecisionSeconds;
                bool chosen = false;
                while (!chosen && timeLeft > 0f)
                {
                    Keyboard kb = Keyboard.current;
                    if (kb != null && kb.pKey.wasPressedThisFrame && player.CanAfford(fine)) { choice = AIActionType.PayDetentionFine; chosen = true; }
                    else if (kb != null && kb.bKey.wasPressedThisFrame && player.HasExemptionTicket) { choice = AIActionType.UseExemptionTicket; chosen = true; }
                    else if (kb != null && kb.tKey.wasPressedThisFrame) chosen = true;
                    timeLeft -= Time.deltaTime;
                    yield return null;
                }
                if (hud != null) hud.StopHumanDecisionTimer();
            }
            else
            {
                choice = ai[player.Index].DecideDetention(Snapshot()).Action;
                yield return new WaitForSeconds(aiPause);
            }

            player.SetInDetention(false);
            if (choice == AIActionType.PayDetentionFine && player.CanAfford(fine))
            {
                player.RemoveMoney(fine);
                setSkipTurn(false);
            }
            else if (choice == AIActionType.UseExemptionTicket && player.ConsumeExemptionTicket())
            {
                setSkipTurn(false);
            }
            else
            {
                setSkipTurn(true); // il passe ce tour et sera libre au suivant
            }
        }

        private IEnumerator SendToDetention(Player player)
        {
            int colle = level.IndexOf(SquareType.Detention);
            player.SetPosition(colle);
            player.SetInDetention(true);
            CampusEvents.RaisePlayerMoved(player.Index, colle, 0, true);

            PawnSplineMover pawn = Pawn(player.Index);
            if (pawn == null || board == null) yield break;
            bool done = false;
            pawn.SendDirectlyToColle(board, colle, () => done = true);
            yield return WaitWithTimeout(() => done, 10f);
        }

        // Déplacement et animations

        private IEnumerator MoveBy(Player player, int steps)
        {
            int n = State.SquareCount;
            int raw = player.Position + steps;
            int to = ((raw % n) + n) % n;
            player.SetPosition(to);
            if (steps > 0 && raw >= n)
            {
                player.AddMoney(level.PassStartBonus);
                CampusEvents.RaisePassedStart(player.Index, level.PassStartBonus);
            }
            CampusEvents.RaisePlayerMoved(player.Index, to, steps, false);

            PawnSplineMover pawn = Pawn(player.Index);
            if (pawn == null || board == null) yield break;
            bool done = false;
            pawn.MoveStepsOnBoard(board, steps, null, _ => done = true);
            yield return WaitWithTimeout(() => done, 15f);
        }

        private IEnumerator AnimateDice(int d1, int d2)
        {
            if (dice == null) yield break;
            bool done = false;
            dice.RollDice(d1, d2, (a, b) => done = true);
            yield return WaitWithTimeout(() => done, 5f);
        }

        private static IEnumerator WaitWithTimeout(System.Func<bool> isDone, float seconds)
        {
            float timeLeft = seconds;
            while (!isDone() && timeLeft > 0f) { timeLeft -= Time.deltaTime; yield return null; }
        }

        /// <summary>Photo de la partie pour l'IA </summary>
        private AIGameSnapshot Snapshot() =>
            AISnapshotBuilder.Build(level, State.Board, State.Players, State.TurnsStarted, State.ActiveEvent, State.UpgradedThisTurn);

        private PawnSplineMover Pawn(int index) =>
            pawns != null && index >= 0 && index < pawns.Length ? pawns[index] : null;

        // Interprétation Saisie Humaine

        private IEnumerator WaitForSpace(string prompt)
        {
            Debug.Log($"[Tour] {prompt}");
            yield return null; // ignore une touche pressée à la frame précédente
            while (Keyboard.current == null || !Keyboard.current.spaceKey.wasPressedThisFrame) yield return null;
        }

        private IEnumerator AskYesNo(string prompt, System.Action<bool> answer)
        {
            Debug.Log($"[Tour] {prompt}");
            if (hud != null) hud.StartHumanDecisionTimer(prompt);
            bool? result = null;
            float timeLeft = humanDecisionSeconds;
            while (result == null && timeLeft > 0f)
            {
                Keyboard kb = Keyboard.current;
                if (kb != null && kb.oKey.wasPressedThisFrame) result = true;
                else if (kb != null && kb.nKey.wasPressedThisFrame) result = false;
                timeLeft -= Time.deltaTime;
                yield return null;
            }
            if (hud != null) hud.StopHumanDecisionTimer();
            answer(result ?? false);
        }

        private IEnumerator Pause(Player player)
        {
            if (!player.IsHuman && aiPause > 0f) yield return new WaitForSeconds(aiPause);
        }

        // Fin de partie, récapitulatif des scores et meilleur score 

        private void EndGame()
        {
            Phase = TurnPhase.GameOver;
            Player winner = State.ActivePlayers
                .OrderByDescending(p => State.NetWorth(p.Index))
                .ThenByDescending(p => State.OwnedBy(p.Index).Count())
                .ThenByDescending(p => p.Money)
                .First();

            int score = State.NetWorth(winner.Index);
            bool byElimination = State.ActivePlayers.Count() == 1;
            if (byElimination)
                score += 50 * Mathf.Max(0, level.MaxTurns - State.TurnsStarted[winner.Index]);

            foreach (Player p in State.Players)
                Debug.Log($"[Fin] {p.Character.DisplayName} : fortune {State.NetWorth(p.Index)}{(p.IsBankrupt ? " (éliminé)" : "")}");
            Debug.Log($"[Fin] Gagnant : {winner.Character.DisplayName}, score {score}.");

            if (winner.IsHuman) SaveBestScore(score);
            CampusEvents.RaiseGameOver(winner.Index, score, winner.IsHuman);
        }

        private void SaveBestScore(int score)
        {
            string key = $"BestScore_Level{level.LevelNumber}";
            if (score <= PlayerPrefs.GetInt(key, 0)) return;
            PlayerPrefs.SetInt(key, score);
            PlayerPrefs.Save();
            Debug.Log($"[Fin] Nouveau meilleur score du niveau {level.LevelNumber} : {score}.");
        }
    }
}