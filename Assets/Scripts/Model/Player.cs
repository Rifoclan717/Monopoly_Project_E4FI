using System;

namespace CampusTycoon
{
    /// <summary>
    /// État joueur pendant une partie
    /// </summary>
    public class Player
    {
        public int Index { get; }
        public CharacterData Character { get; }
        public bool IsHuman { get; }

        public int Money { get; private set; }
        public int Position { get; private set; }
        public bool InDetention { get; private set; }
        public bool PowerUsed { get; private set; }
        public bool HasExemptionTicket { get; private set; }
        public bool MustSkipNextTurn { get; private set; }
        public bool IsBankrupt { get; private set; }

        public bool CanUsePower => !PowerUsed && !IsBankrupt;

        public Player(int index, CharacterData character, bool isHuman, int startingMoney)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            if (startingMoney < 0) throw new ArgumentOutOfRangeException(nameof(startingMoney));

            Index = index;
            Character = character;
            IsHuman = isHuman;
            Money = startingMoney;
        }

        // Argent 

        public bool CanAfford(int amount) => Money >= amount;

        public void AddMoney(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "Utiliser RemoveMoney pour retirer de l'argent.");
            if (amount == 0) return;
            SetMoney(Money + amount);
        }

        /// <summary>Gestion du solde </summary>
        public void RemoveMoney(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "Utiliser AddMoney pour ajouter de l'argent.");
            if (amount == 0) return;
            SetMoney(Money - amount);
        }

        private void SetMoney(int newAmount)
        {
            int oldAmount = Money;
            Money = newAmount;
            CampusEvents.RaiseMoneyChanged(Index, newAmount, newAmount - oldAmount);
        }

        // Position

        public void SetPosition(int squareIndex)
        {
            if (squareIndex < 0) throw new ArgumentOutOfRangeException(nameof(squareIndex));
            Position = squareIndex;
        }

        // Colle 

        public void SetInDetention(bool value)
        {
            if (InDetention == value) return;
            InDetention = value;
            CampusEvents.RaiseDetentionChanged(Index, value);
        }

        // Pouvoir

        public void UsePower()
        {
            if (!CanUsePower) throw new InvalidOperationException($"Le joueur {Index} ne peut plus utiliser son pouvoir.");
            PowerUsed = true;
            CampusEvents.RaisePowerUsed(Index, Character.DisplayName, Character.PowerName, Character.PowerDescription);
        }

        // Effets des cartes 

        public void GiveExemptionTicket() => HasExemptionTicket = true;

        /// <summary>Utilise du billet d'absence </summary>
        public bool ConsumeExemptionTicket()
        {
            if (!HasExemptionTicket) return false;
            HasExemptionTicket = false;
            return true;
        }

        public void SetSkipNextTurn(bool value) => MustSkipNextTurn = value;

        //  Scénario Fin de jeu 

        /// <summary>Élimine le joueur. creditorIndex = joueur créancier ou CampusEvents.BankIndex.</summary>
        public void DeclareBankrupt(int creditorIndex)
        {
            if (IsBankrupt) return;
            IsBankrupt = true;
            CampusEvents.RaisePlayerBankrupt(Index, creditorIndex);
        }
    }
}