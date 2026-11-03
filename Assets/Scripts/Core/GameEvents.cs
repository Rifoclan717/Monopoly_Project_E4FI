using System;

namespace CampusTycoon
{
    /// <summary>
    /// Bus d'événements du jeu (pattern Observer).
    /// La logique appelle les méthodes Raise...(). L'affichage, le son et l'IA s'abonnent.
    ///
    /// Toujours se désabonner dans OnDisable : les événements statiques survivent
    /// au changement de scène et garderaient sinon une référence à un objet détruit.
    /// </summary>
    public static class GameEvents
    {
        public const int BankIndex = -1;

        /// <summary>L'argent d'un joueur a changé </summary>
        public static event Action<int, int, int> OnMoneyChanged;

        /// <summary>Un joueur entre en colle ou en sort </summary>
        public static event Action<int, bool> OnDetentionChanged;

        /// <summary>Un joueur utilise son pouvoir </summary>
        public static event Action<int, PowerType> OnPowerUsed;

        /// <summary>Un joueur fait faillite </summary>
        public static event Action<int, int> OnPlayerBankrupt;

        public static void RaiseMoneyChanged(int playerIndex, int oldAmount, int newAmount) =>
            OnMoneyChanged?.Invoke(playerIndex, oldAmount, newAmount);

        public static void RaiseDetentionChanged(int playerIndex, bool inDetention) =>
            OnDetentionChanged?.Invoke(playerIndex, inDetention);

        public static void RaisePowerUsed(int playerIndex, PowerType power) =>
            OnPowerUsed?.Invoke(playerIndex, power);

        public static void RaisePlayerBankrupt(int playerIndex, int creditorIndex) =>
            OnPlayerBankrupt?.Invoke(playerIndex, creditorIndex);
    }
}