using System;

/// <summary>
/// Bus d'événements Observer officiel de Campus Tycoon.
/// Découple à 100% la Vue 3D / HUD de la Logique Métier et de l'IA.
/// </summary>
public static class CampusEvents
{
    // 1. Début d'un tour (playerId: 0=Humain, 1=IA1, 2=IA2, turnNumber: 1..15, maxTurns: 12 ou 15)
    public static event Action<int, int, int> OnTurnStarted;

    // 2. Résultat des dés (playerId, die1, die2, isDouble)
    public static event Action<int, int, int, bool> OnDiceRolled;

    // 3. Déplacement d'un pion (playerId, targetTileIndex, stepsDelta, sentToColle)
    public static event Action<int, int, int, bool> OnPlayerMoved;

    // 4. Changement d'argent (playerId, newMoneyAmount, deltaAmount)
    public static event Action<int, int, int> OnMoneyChanged;

    // 5. Achat d'un terrain (playerId, tileIndex, pricePaid)
    public static event Action<int, int, int> OnPropertyBought;

    // 6. Construction / Amélioration (playerId, tileIndex, newBuildingLevel 1..3)
    public static event Action<int, int, int> OnPropertyUpgraded;

    // 7. Paiement d'un loyer (tenantId, ownerId, tileIndex, rentAmount)
    public static event Action<int, int, int, int> OnRentPaid;

    // 8. Carte Événement piochée (playerId, cardNumber 1..12, cardTitle, cardDescription)
    public static event Action<int, int, string, string> OnCardDrawn;

    // 9. Événement du semestre (eventTitle, eventEffectDescription)
    public static event Action<string, string> OnGlobalEventStarted;

    // 10. Pouvoir d'étudiant utilisé (playerId, characterName, powerName, powerDescription)
    public static event Action<int, string, string, string> OnPowerUsed;

    // 11. Faillite d'un étudiant (playerId, creditorId)
    public static event Action<int, int> OnPlayerBankrupt;

    // 12. Fin de partie (winnerPlayerId, finalScore, isHumanVictory)
    public static event Action<int, int, bool> OnGameOver;

    // ==================== MÉTHODES D'ÉMISSION (Appels depuis Logique ou Testeur) ====================

    public static void RaiseTurnStarted(int playerId, int turnNumber, int maxTurns = 15)
        => OnTurnStarted?.Invoke(playerId, turnNumber, maxTurns);

    public static void RaiseDiceRolled(int playerId, int die1, int die2)
        => OnDiceRolled?.Invoke(playerId, die1, die2, die1 == die2);

    public static void RaisePlayerMoved(int playerId, int targetTileIndex, int stepsDelta, bool sentToColle = false)
        => OnPlayerMoved?.Invoke(playerId, targetTileIndex, stepsDelta, sentToColle);

    public static void RaiseMoneyChanged(int playerId, int newMoney, int delta)
        => OnMoneyChanged?.Invoke(playerId, newMoney, delta);

    public static void RaisePropertyBought(int playerId, int tileIndex, int price)
        => OnPropertyBought?.Invoke(playerId, tileIndex, price);

    public static void RaisePropertyUpgraded(int playerId, int tileIndex, int newLevel)
        => OnPropertyUpgraded?.Invoke(playerId, tileIndex, newLevel);

    public static void RaiseRentPaid(int tenantId, int ownerId, int tileIndex, int amount)
        => OnRentPaid?.Invoke(tenantId, ownerId, tileIndex, amount);

    public static void RaiseCardDrawn(int playerId, int cardId, string title, string effect)
        => OnCardDrawn?.Invoke(playerId, cardId, title, effect);

    public static void RaiseGlobalEventStarted(string title, string effect)
        => OnGlobalEventStarted?.Invoke(title, effect);

    public static void RaisePowerUsed(int playerId, string charName, string powerName, string desc)
        => OnPowerUsed?.Invoke(playerId, charName, powerName, desc);

    public static void RaisePlayerBankrupt(int playerId, int creditorId = -1)
        => OnPlayerBankrupt?.Invoke(playerId, creditorId);

    public static void RaiseGameOver(int winnerId, int finalScore, bool isHumanVictory)
        => OnGameOver?.Invoke(winnerId, finalScore, isHumanVictory);
}