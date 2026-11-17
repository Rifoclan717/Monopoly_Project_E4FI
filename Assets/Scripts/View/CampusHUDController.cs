using System.Collections.Generic;
using CampusTycoon;
using CampusTycoon.AI;
using UnityEngine;
using UnityEngine.InputSystem;

public class CampusHUDController : MonoBehaviour
{
    [Header("État Affiché par le HUD (Abonné à CampusEvents)")]
    public string[] studentNames = { "Vous (Major de promo)", "IA 1 (Fêtard du BDE)", "IA 2 (Le Techos)" };
    public int[] studentMoney = { 1000, 1000, 1000 };
    public int currentPlayerTurn = 0;
    public int currentTurnNumber = 1;
    public int maxTurns = 15;
    public int bestScore = 0;

    [Header("Accélération des Animations pendant les Tours d'IA (Tâche 6)")]
    [Tooltip("Si activé, accélère automatiquement les pions et les dés pendant les tours des rivaux IA")]
    public bool accelerateAITurns = true;
    [Range(1f, 5f)] public float aiTurnSpeedMultiplier = 2.2f;
    [Tooltip("Mode Turbo global activable avec la touche [T] ou le bouton HUD")]
    public bool turboModeActive = false;
    [Range(2f, 8f)] public float turboSpeedMultiplier = 4f;

    [Header("Références Automatiques (Trouvées si vides)")]
    public TurnManager turnManager;
    public ProceduralBoardGenerator board;
    public ProceduralDiceRoller diceRoller;
    public AIBarkController barkController;

    private bool initializedFromTurnManager = false;

    // Bannière d'événement global du semestre
    private string activeSemesterEventTitle = "";
    private string activeSemesterEventEffect = "";

    // Pop-up Carte Événement piochée
    private string lastCardBanner = "";
    private float cardBannerTimer = 0f;

    // Zone 1 : Bandeau d'information HUD (achats, constructions, loyers, pouvoirs, faillites)
    // Séparé de la bulle IA pour ne jamais effacer une réplique d'AIBarkController en cours (Tâche 8)
    private string infoMessageText = "";
    private float infoMessageTimer = 0f;

    // Zone 2 : Bulle de réplique IA (alimentée par AIBarkController via ShowAISpeech)
    private string aiSpeechBubbleText = "";
    private float aiBubbleTimer = 0f;
    private readonly Queue<(string text, float duration)> aiSpeechQueue = new Queue<(string text, float duration)>();

    // Chrono de décision humain (10 secondes max selon les règles)
    private bool decisionTimerActive = false;
    private float decisionTimeRemaining = 10f;
    private string decisionPrompt = "";

    // Écran de fin de partie
    private bool isGameOver = false;
    private string gameOverMessage = "";

    void OnEnable()
    {
        bestScore = PlayerPrefs.GetInt("BestScore_Level2", 0);

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

        BindBarkControllerIfAvailable();
    }

    void Start()
    {
        if (turnManager == null) turnManager = FindFirstObjectByType<TurnManager>();
        if (board == null) board = FindFirstObjectByType<ProceduralBoardGenerator>();
        if (diceRoller == null) diceRoller = FindFirstObjectByType<ProceduralDiceRoller>();

        BindBarkControllerIfAvailable();
        TrySyncWithTurnManager();
        ApplyAnimationSpeedForPlayer(currentPlayerTurn);
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

        if (barkController != null && barkController.OnSpeech != null)
        {
            barkController.OnSpeech.RemoveListener(ShowAISpeech);
        }
    }

    /// <summary>
    /// Relie automatiquement AIBarkController.OnSpeech à ShowAISpeech (et le crée si TurnManager est présent).
    /// </summary>
    public void BindBarkControllerIfAvailable()
    {
        if (barkController == null)
        {
            barkController = FindFirstObjectByType<AIBarkController>();
            if (barkController == null && Application.isPlaying)
            {
                barkController = gameObject.AddComponent<AIBarkController>();
            }
        }
        if (barkController != null && barkController.OnSpeech != null)
        {
            barkController.OnSpeech.RemoveListener(ShowAISpeech);
            barkController.OnSpeech.AddListener(ShowAISpeech);
        }
    }

    private void TrySyncWithTurnManager()
    {
        if (initializedFromTurnManager) return;
        if (turnManager == null) turnManager = FindFirstObjectByType<TurnManager>();
        if (turnManager == null || turnManager.State == null) return;

        GameState state = turnManager.State;
        if (state.Level != null)
        {
            maxTurns = state.Level.MaxTurns;
            bestScore = PlayerPrefs.GetInt($"BestScore_Level{state.Level.LevelNumber}", 0);
        }

        for (int i = 0; i < state.Players.Count && i < 3; i++)
        {
            Player p = state.Players[i];
            string roleTag = p.IsHuman ? "Vous" : $"IA {i}";
            studentNames[i] = $"{roleTag} ({p.Character.DisplayName})";
            studentMoney[i] = p.Money;
        }

        BindBarkControllerIfAvailable();
        if (barkController != null)
        {
            AIController[] rivals = FindObjectsByType<AIController>(FindObjectsSortMode.None);
            barkController.Bind(state.Board, rivals);
        }

        initializedFromTurnManager = true;
    }

    void Update()
    {
        if (!initializedFromTurnManager)
        {
            TrySyncWithTurnManager();
        }

        if (cardBannerTimer > 0f) cardBannerTimer -= Time.deltaTime;
        if (infoMessageTimer > 0f) infoMessageTimer -= Time.deltaTime;

        if (aiBubbleTimer > 0f)
        {
            aiBubbleTimer -= Time.deltaTime;
            if (aiBubbleTimer <= 0f && aiSpeechQueue.Count > 0)
            {
                var next = aiSpeechQueue.Dequeue();
                aiSpeechBubbleText = next.text;
                aiBubbleTimer = next.duration;
            }
        }

        if (decisionTimerActive)
        {
            decisionTimeRemaining -= Time.deltaTime;
            if (decisionTimeRemaining <= 0f)
            {
                decisionTimeRemaining = 0f;
                decisionTimerActive = false;
                ShowInfoMessage("Temps écoulé (10s) : décision automatique « Passer ».");
            }
        }

        // Raccourci clavier [V] pour activer/désactiver le mode Turbo sans interférer avec [T] (attendre en colle)
        if (Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame)
        {
            turboModeActive = !turboModeActive;
            ApplyAnimationSpeedForPlayer(currentPlayerTurn);
        }
    }

    private void ApplyAnimationSpeedForPlayer(int playerId)
    {
        float targetSpeed = 1f;
        if (turboModeActive)
        {
            targetSpeed = turboSpeedMultiplier;
        }
        else if (accelerateAITurns && playerId != 0)
        {
            targetSpeed = aiTurnSpeedMultiplier;
        }

        if (board == null) board = FindFirstObjectByType<ProceduralBoardGenerator>();
        if (board != null)
        {
            board.SetAllPawnsSpeedMultiplier(targetSpeed);
        }

        if (diceRoller == null) diceRoller = FindFirstObjectByType<ProceduralDiceRoller>();
        if (diceRoller != null)
        {
            diceRoller.speedMultiplier = targetSpeed;
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

    // ==================== RÉACTIONS AUX ÉVÉNEMENTS OBSERVER (TÂCHES 6 & 8) ====================

    private void HandleTurnStarted(int playerId, int turn, int totalTurns)
    {
        TrySyncWithTurnManager();
        currentPlayerTurn = Mathf.Clamp(playerId, 0, 2);
        currentTurnNumber = turn;
        maxTurns = totalTurns;
        ApplyAnimationSpeedForPlayer(currentPlayerTurn);
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
        ShowInfoMessage($"✅ Terrain (Case {tileIndex}) acheté pour {price}€ !");
    }

    private void HandlePropertyUpgraded(int playerId, int tileIndex, int newLevel)
    {
        StopHumanDecisionTimer();
        ShowInfoMessage($"🏗️ Case {tileIndex} améliorée au Niveau {newLevel} ! (Loyers augmentés)");
    }

    private void HandleRentPaid(int tenantId, int ownerId, int tileIndex, int amount)
    {
        ShowInfoMessage($"💸 Loyer payé : {amount}€ (Case {tileIndex}) !");
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
        ShowInfoMessage($"⚡ POUVOIR [{charName}] — {powerName} : {desc}");
    }

    private void HandlePlayerBankrupt(int playerId, int creditorId)
    {
        StopHumanDecisionTimer();
        ShowInfoMessage($"💀 {studentNames[Mathf.Clamp(playerId, 0, 2)]} est éliminé (Faillite) !");
    }

    private void HandleGameOver(int winnerId, int finalScore, bool isHumanVictory)
    {
        isGameOver = true;
        int levelNum = (turnManager != null && turnManager.State != null && turnManager.State.Level != null)
            ? turnManager.State.Level.LevelNumber
            : 2;
        string key = $"BestScore_Level{levelNum}";

        if (isHumanVictory && finalScore > bestScore)
        {
            bestScore = finalScore;
            PlayerPrefs.SetInt(key, bestScore);
            PlayerPrefs.Save();
        }
        else
        {
            bestScore = Mathf.Max(bestScore, PlayerPrefs.GetInt(key, 0));
        }

        gameOverMessage = isHumanVictory
            ? $"🏆 Vous décrochez la bourse d'excellence ! (Score : {finalScore})"
            : $"🎓 {studentNames[Mathf.Clamp(winnerId, 0, 2)]} est premier du classement (Score : {finalScore})…";
    }

    /// <summary>
    /// Affiche un message d'information système du HUD (achat, amélioration, loyer, pouvoir, faillite)
    /// dans son propre bandeau, sans écraser la bulle de réplique d'IA en cours.
    /// </summary>
    public void ShowInfoMessage(string msg, float duration = 3.5f)
    {
        infoMessageText = msg;
        infoMessageTimer = duration;
    }

    /// <summary>
    /// Affiche une bulle de réplique d'IA en bas de l'écran (appelée par AIBarkController.OnSpeech).
    /// Si une réplique est déjà affichée, la nouvelle est mise en file d'attente pour ne rien couper.
    /// </summary>
    public void ShowAISpeech(string msg, float duration = 4.5f)
    {
        if (string.IsNullOrEmpty(msg)) return;

        if (aiBubbleTimer > 0.25f && aiSpeechBubbleText != msg)
        {
            aiSpeechQueue.Enqueue((msg, duration));
            return;
        }

        aiSpeechBubbleText = msg;
        aiBubbleTimer = duration;
    }

    // ==================== RENDU HUD IMMÉDIAT (Zéro configuration de Canvas requise) ====================

    void OnGUI()
    {
        GUIStyle bannerStyle = new GUIStyle(GUI.skin.box) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        GUI.color = Color.white;

        // 1. Panneau Haut-Gauche : Les 3 Étudiants, Tour, Best Score & Options d'Accélération IA
        GUILayout.BeginArea(new Rect(15, 15, 370, 235), GUI.skin.box);
        GUILayout.Label($"🎓 CAMPUS TYCOON — Tour {currentTurnNumber} / {maxTurns}   |   🏆 Best: {bestScore}");
        GUILayout.Space(4);
        for (int i = 0; i < 3; i++)
        {
            string turnTag = (i == currentPlayerTurn) ? "▶ " : "   ";
            GUILayout.Label($"{turnTag}{studentNames[i]} : {studentMoney[i]} €");
        }
        GUILayout.Space(6);

        // Option Tâche 6 : Accélérer les animations pendant les tours d'IA + Mode Turbo
        GUILayout.BeginHorizontal();
        bool newAccelAI = GUILayout.Toggle(accelerateAITurns, $" Accélérer tours IA (x{aiTurnSpeedMultiplier:0.0})");
        bool newTurbo = GUILayout.Toggle(turboModeActive, $" Turbo [V] (x{turboSpeedMultiplier:0})");
        GUILayout.EndHorizontal();

        if (newAccelAI != accelerateAITurns || newTurbo != turboModeActive)
        {
            accelerateAITurns = newAccelAI;
            turboModeActive = newTurbo;
            ApplyAnimationSpeedForPlayer(currentPlayerTurn);
        }

        GUILayout.Space(4);
        if (turnManager != null)
        {
            GUILayout.Label("Commandes Partie : [Espace] Lancer dés | [Clic] Passer anim dé\nAchat : [O] Oui / [N] Non | Colle : [P] Payer [B] Billet [T] Attendre");
        }
        else
        {
            GUILayout.Label("Contrôles Démo : [Espace] Dés+Avancer | [A] Acheter/Bâtir\n[Tab] Changer Pion (0..2) | [E] Semestre | [K] Carte | [C] Colle");
        }
        GUILayout.EndArea();

        // 2. Bannière Événement du Semestre (Haut-Centre)
        if (!string.IsNullOrEmpty(activeSemesterEventTitle))
        {
            GUI.backgroundColor = new Color(0.95f, 0.72f, 0.15f);
            GUI.Box(new Rect(Screen.width * 0.5f - 260, 15, 520, 42),
                $"📢 ÉVÉNEMENT DU SEMESTRE : {activeSemesterEventTitle} ({activeSemesterEventEffect})", bannerStyle);
            GUI.backgroundColor = Color.white;
        }

        // 2b. Invitation au lancer de dés quand TurnManager attend [Espace] du joueur humain
        if (turnManager != null && turnManager.Phase == TurnManager.TurnPhase.Roll && currentPlayerTurn == 0 && !decisionTimerActive)
        {
            GUI.backgroundColor = new Color(0.18f, 0.52f, 0.95f);
            GUI.Box(new Rect(Screen.width * 0.5f - 230, Screen.height - 155, 460, 42),
                "🎲 C'est votre tour ! Appuyez sur [Espace] pour lancer les dés", bannerStyle);
            GUI.backgroundColor = Color.white;
        }

        // 3. Chrono de décision humain de 10 secondes
        if (decisionTimerActive)
        {
            GUI.backgroundColor = new Color(0.2f, 0.65f, 0.95f);
            GUI.Box(new Rect(Screen.width * 0.5f - 270, Screen.height - 155, 540, 42),
                $"⏱️ {decisionPrompt} ({decisionTimeRemaining:0.0}s)", bannerStyle);
            GUI.backgroundColor = Color.white;
        }

        // 4. Pop-up Carte Événement piochée
        if (cardBannerTimer > 0f)
        {
            GUI.backgroundColor = new Color(0.65f, 0.35f, 0.95f);
            GUI.Box(new Rect(Screen.width * 0.5f - 280, 68, 560, 38), lastCardBanner, bannerStyle);
            GUI.backgroundColor = Color.white;
        }

        // 5a. Zone Information HUD (Actions du jeu : achat, construction, loyer, pouvoir)
        if (infoMessageTimer > 0f)
        {
            GUI.backgroundColor = new Color(0.22f, 0.32f, 0.48f);
            GUI.Box(new Rect(Screen.width * 0.5f - 260, Screen.height - 104, 520, 36), infoMessageText, bannerStyle);
            GUI.backgroundColor = Color.white;
        }

        // 5b. Zone Bulle de Réplique IA (Alimentée par AIBarkController via ShowAISpeech)
        if (aiBubbleTimer > 0f)
        {
            GUI.backgroundColor = new Color(0.15f, 0.85f, 0.55f);
            GUI.Box(new Rect(Screen.width * 0.5f - 260, Screen.height - 58, 520, 40), $"💬 {aiSpeechBubbleText}", bannerStyle);
            GUI.backgroundColor = Color.white;
        }

        // 6. Écran de Fin de Partie
        if (isGameOver)
        {
            GUI.Box(new Rect(Screen.width * 0.5f - 250, Screen.height * 0.5f - 50, 500, 100), gameOverMessage, bannerStyle);
        }
    }
}
