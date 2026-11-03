using CampusTycoon;
using UnityEngine;

public class CampusHUDController : MonoBehaviour
{
    [Header("État Affiché par le HUD (Abonné à CampusEvents)")]
    public string[] studentNames = { "Vous (Major de promo)", "IA 1 (Fêtard du BDE)", "IA 2 (Le Techos)" };
    public int[] studentMoney = { 1000, 1000, 1000 };
    public int currentPlayerTurn = 0;
    public int currentTurnNumber = 1;
    public int maxTurns = 15;
    public int bestScore = 0;

    // Bannière d'événement global du semestre
    private string activeSemesterEventTitle = "";
    private string activeSemesterEventEffect = "";

    // Pop-up Carte Événement piochée
    private string lastCardBanner = "";
    private float cardBannerTimer = 0f;

    // Bulle de réplique IA
    private string aiSpeechBubbleText = "";
    private float aiBubbleTimer = 0f;

    // Chrono de décision humain (10 secondes max selon les règles)
    private bool decisionTimerActive = false;
    private float decisionTimeRemaining = 10f;
    private string decisionPrompt = "";

    // Écran de fin de partie
    private bool isGameOver = false;
    private string gameOverMessage = "";

    void OnEnable()
    {
        bestScore = PlayerPrefs.GetInt("BestScore_Level2", 1450);

        CampusEvents.OnTurnStarted += HandleTurnStarted;
        CampusEvents.OnMoneyChanged += HandleMoneyChanged;
        CampusEvents.OnPropertyBought += HandlePropertyBought;
        CampusEvents.OnPropertyUpgraded += HandlePropertyUpgraded;
        CampusEvents.OnRentPaid += HandleRentPaid;
        CampusEvents.OnCardDrawn += HandleCardDrawn;
        CampusEvents.OnGlobalEventStarted += HandleGlobalEventStarted;
        CampusEvents.OnPowerUsed += HandlePowerUsed;
        CampusEvents.OnPlayerBankrupt += HandlePlayerBankrupt;
        CampusEvents.OnGameOver += HandleGameOver;
    }

    void OnDisable()
    {
        CampusEvents.OnTurnStarted -= HandleTurnStarted;
        CampusEvents.OnMoneyChanged -= HandleMoneyChanged;
        CampusEvents.OnPropertyBought -= HandlePropertyBought;
        CampusEvents.OnPropertyUpgraded -= HandlePropertyUpgraded;
        CampusEvents.OnRentPaid -= HandleRentPaid;
        CampusEvents.OnCardDrawn -= HandleCardDrawn;
        CampusEvents.OnGlobalEventStarted -= HandleGlobalEventStarted;
        CampusEvents.OnPowerUsed -= HandlePowerUsed;
        CampusEvents.OnPlayerBankrupt -= HandlePlayerBankrupt;
        CampusEvents.OnGameOver -= HandleGameOver;
    }

    void Update()
    {
        if (cardBannerTimer > 0f) cardBannerTimer -= Time.deltaTime;
        if (aiBubbleTimer > 0f) aiBubbleTimer -= Time.deltaTime;

        if (decisionTimerActive)
        {
            decisionTimeRemaining -= Time.deltaTime;
            if (decisionTimeRemaining <= 0f)
            {
                decisionTimeRemaining = 0f;
                decisionTimerActive = false;
                ShowAISpeech("Temps écoulé (10s) : décision automatique « Passer ».");
            }
        }
    }

    public void StartHumanDecisionTimer(string promptText)
    {
        decisionPrompt = promptText;
        decisionTimeRemaining = 10f;
        decisionTimerActive = true;
    }

    public void StopHumanDecisionTimer()
    {
        decisionTimerActive = false;
    }

    // ==================== RÉACTIONS AUX ÉVÉNEMENTS OBSERVER ====================

    private void HandleTurnStarted(int playerId, int turn, int totalTurns)
    {
        currentPlayerTurn = Mathf.Clamp(playerId, 0, 2);
        currentTurnNumber = turn;
        maxTurns = totalTurns;
    }

    private void HandleMoneyChanged(int playerId, int newMoney, int delta)
    {
        if (playerId >= 0 && playerId < studentMoney.Length)
        {
            studentMoney[playerId] = newMoney;
        }
    }

    private void HandlePropertyBought(int playerId, int tileIndex, int price)
    {
        StopHumanDecisionTimer();
        if (playerId == 1) ShowAISpeech("🔥 Fêtard du BDE : « Soirée chez moi ce soir sur cette case ! »");
        else if (playerId == 2) ShowAISpeech("🤓 Le Techos : « Investissement rentable calculé à 100 %. »");
        else ShowAISpeech($"✅ Terrain (Case {tileIndex}) acheté pour {price}€ !");
    }

    private void HandlePropertyUpgraded(int playerId, int tileIndex, int newLevel)
    {
        StopHumanDecisionTimer();
        ShowAISpeech($"🏗️ Case {tileIndex} améliorée au Niveau {newLevel} ! (Loyers augmentés)");
        if (playerId == 2) ShowAISpeech("🤓 Le Techos : « Tu vas regretter cet amphi… »");
    }

    private void HandleRentPaid(int tenantId, int ownerId, int tileIndex, int amount)
    {
        ShowAISpeech($"💸 Loyer payé : {amount}€ ! « Merci pour le loyer ! »");
    }

    private void HandleCardDrawn(int playerId, int cardId, string title, string effect)
    {
        lastCardBanner = $"🃏 CARTE ÉVÉNEMENT #{cardId} — {title} : {effect}";
        cardBannerTimer = 5.5f;
    }

    private void HandleGlobalEventStarted(string title, string effect)
    {
        activeSemesterEventTitle = title;
        activeSemesterEventEffect = effect;
    }

    private void HandlePowerUsed(int playerId, string charName, string powerName, string desc)
    {
        ShowAISpeech($"⚡ POUVOIR [{charName}] — {powerName} : {desc}");
    }

    private void HandlePlayerBankrupt(int playerId, int creditorId)
    {
        StopHumanDecisionTimer();
        ShowAISpeech($"💀 {studentNames[Mathf.Clamp(playerId, 0, 2)]} est éliminé (Faillite) !");
    }

    private void HandleGameOver(int winnerId, int finalScore, bool isHumanVictory)
    {
        isGameOver = true;
        if (isHumanVictory && finalScore > bestScore)
        {
            bestScore = finalScore;
            PlayerPrefs.SetInt("BestScore_Level2", bestScore);
            PlayerPrefs.Save();
        }

        gameOverMessage = isHumanVictory
            ? $"🏆 Vous décrochez la bourse d'excellence ! (Score : {finalScore})"
            : $"🎓 {studentNames[Mathf.Clamp(winnerId, 0, 2)]} est premier du classement…";
    }

    /// <summary>
    /// Affiche une bulle de réplique d'IA ou de notification en bas de l'écran.
    /// Public pour permettre au système de répliques d'IA (Malick) de l'appeler directement.
    /// </summary>
    public void ShowAISpeech(string msg, float duration = 4.5f)
    {
        aiSpeechBubbleText = msg;
        aiBubbleTimer = duration;
    }

    // ==================== RENDU HUD IMMÉDIAT (Zéro configuration de Canvas requise) ====================

    void OnGUI()
    {
        GUIStyle boxStyle = new GUIStyle(GUI.skin.box) { fontSize = 13, alignment = TextAnchor.UpperLeft };
        GUIStyle bannerStyle = new GUIStyle(GUI.skin.box) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        GUI.color = Color.white;

        // 1. Panneau Haut-Gauche : Les 3 Étudiants, Tour & Best Score
        GUILayout.BeginArea(new Rect(15, 15, 355, 200), GUI.skin.box);
        GUILayout.Label($"🎓 CAMPUS TYCOON — Tour {currentTurnNumber} / {maxTurns}   |   🏆 Best: {bestScore}");
        GUILayout.Space(6);
        for (int i = 0; i < 3; i++)
        {
            string turnTag = (i == currentPlayerTurn) ? "▶ " : "   ";
            GUILayout.Label($"{turnTag}{studentNames[i]} : {studentMoney[i]} €");
        }
        GUILayout.Space(8);
        GUILayout.Label("Contrôles Démo : [Espace] Dés+Avancer | [A] Acheter/Bâtir\n[Tab] Changer Pion (0..2) | [E] Semestre | [K] Carte | [C] Colle | [T] Turbo");
        GUILayout.EndArea();

        // 2. Bannière Événement du Semestre (Haut-Centre)
        if (!string.IsNullOrEmpty(activeSemesterEventTitle))
        {
            GUI.backgroundColor = new Color(0.95f, 0.72f, 0.15f);
            GUI.Box(new Rect(Screen.width * 0.5f - 260, 15, 520, 42),
                $"📢 ÉVÉNEMENT DU SEMESTRE : {activeSemesterEventTitle} ({activeSemesterEventEffect})", bannerStyle);
            GUI.backgroundColor = Color.white;
        }

        // 3. Chrono de décision humain de 10 secondes
        if (decisionTimerActive)
        {
            GUI.backgroundColor = new Color(0.2f, 0.65f, 0.95f);
            GUI.Box(new Rect(Screen.width * 0.5f - 230, Screen.height - 115, 460, 45),
                $"⏱️ {decisionPrompt} — Temps restant : {decisionTimeRemaining:0.0}s (Appuie sur [A])", bannerStyle);
            GUI.backgroundColor = Color.white;
        }

        // 4. Pop-up Carte Événement piochée
        if (cardBannerTimer > 0f)
        {
            GUI.backgroundColor = new Color(0.65f, 0.35f, 0.95f);
            GUI.Box(new Rect(Screen.width * 0.5f - 280, 68, 560, 38), lastCardBanner, bannerStyle);
            GUI.backgroundColor = Color.white;
        }

        // 5. Bulle de Réplique IA / Notification Action
        if (aiBubbleTimer > 0f)
        {
            GUI.backgroundColor = new Color(0.15f, 0.85f, 0.55f);
            GUI.Box(new Rect(Screen.width * 0.5f - 260, Screen.height - 60, 520, 38), aiSpeechBubbleText, bannerStyle);
            GUI.backgroundColor = Color.white;
        }

        // 6. Écran de Fin de Partie
        if (isGameOver)
        {
            GUI.Box(new Rect(Screen.width * 0.5f - 250, Screen.height * 0.5f - 50, 500, 100), gameOverMessage, bannerStyle);
        }
    }
}
