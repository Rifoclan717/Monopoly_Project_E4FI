using System.Globalization;

namespace CampusTycoon.AI
{
    public enum AIActionType
    {
        Buy,
        Pass,
        Upgrade,
        PayDetentionFine,
        UseExemptionTicket,
        WaitInDetention,
        UsePower,
        KeepPower,
        SellUpgrade,
        SellProperty,
        PlayRoulette,
        SkipRoulette
    }

    /// <summary>Décision renvoyée par l'IA : action, score et justification courte.</summary>
    public sealed class AIDecision
    {
        public readonly AIActionType Action;
        /// <summary>Case concernée, ou -1.</summary>
        public readonly int Square;
        public readonly double Score;
        public readonly string Reason;

        public AIDecision(AIActionType action, int square, double score, string reason)
        {
            Action = action;
            Square = square;
            Score = score;
            Reason = reason ?? string.Empty;
        }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "{0}{1} (score {2:0.#}) : {3}",
                Action, Square >= 0 ? " #" + Square : string.Empty, Score, Reason);
    }

    /// <summary>Moment où l'IA peut déclencher son pouvoir.</summary>
    public enum PowerMoment
    {
        /// <summary>Un rival va payer un loyer à l'IA (Major : Dossier béton).</summary>
        CollectingRent,
        /// <summary>L'IA doit payer un loyer (Fêtard : Soirée mémorable).</summary>
        PayingRent,
        /// <summary>L'IA doit payer une taxe (Fêtard : Soirée mémorable).</summary>
        PayingTax,
        /// <summary>Les dés viennent d'être lancés, avant le déplacement (Techos : Relance).</summary>
        DiceRolled
    }

    /// <summary>Contexte transmis par la logique quand un pouvoir peut être utilisé.</summary>
    public sealed class PowerContext
    {
        public PowerMoment Moment;
        /// <summary>Loyer encaissé ou payé, ou taxe.</summary>
        public int Amount;
        /// <summary>DiceRolled : position avant le déplacement.</summary>
        public int FromPosition = -1;
        /// <summary>DiceRolled : total des dés obtenu.</summary>
        public int DiceTotal;
    }
}
