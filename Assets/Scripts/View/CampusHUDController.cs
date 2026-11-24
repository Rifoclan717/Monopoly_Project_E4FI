using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CampusTycoon;
using CampusTycoon.AI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Interface complète UGUI responsive de Campus Tycoon :
/// - Menu Principal (choix du niveau 1..3 + personnage + aperçu 3D en direct)
/// - HUD In-Game (3 cartes joueurs, fortune, badges Pouvoir/Billet/Colle, barre de décision cliquable,
///   bannière Événement du semestre auto-effacée, carte Événement, info système et bulle répliques IA)
/// - Inspecteur de case (carte de propriété au clic sur une tuile 3D)
/// - Menu Pause ([Échap]), Aide Règles ([H]) et Écran de Fin de Partie complet (podium + Rejouer/Suivant/Menu/Quitter)
/// </summary>
public class CampusHUDController : MonoBehaviour
{
    [Header("État Affiché par le HUD (Abonné à CampusEvents)")]
    public string[] studentNames = { "Vous (Le Major de promo)", "IA 1 (Le Fêtard du BDE)", "IA 2 (Le Techos)" };
    public int[] studentMoney = { 1000, 1000, 1000 };
    public int currentPlayerTurn = 0;
    public int currentTurnNumber = 1;
    public int maxTurns = 15;
    public int bestScore = 0;

    [Header("Accélération des Animations pendant les Tours d'IA")]
    [Tooltip("Désactivé par défaut pour un rythme IA réaliste (4 à 6 s par tour). Activable en jeu.")]
    public bool accelerateAITurns = false;
    [Range(1f, 5f)] public float aiTurnSpeedMultiplier = 1.6f;
    [Tooltip("Mode Turbo global activable avec la touche [V] ou le bouton HUD")]
    public bool turboModeActive = false;
    [Range(2f, 8f)] public float turboSpeedMultiplier = 3.5f;
    [Tooltip("Temps de réflexion IA en mode normal / rapide / turbo (secondes)")]
    public float normalAIPauseSeconds = 1.55f;
    public float fastAIPauseSeconds = 0.75f;
    public float turboAIPauseSeconds = 0.25f;

    [Header("Références Automatiques (Trouvées si vides)")]
    public TurnManager turnManager;
    public ProceduralBoardGenerator board;
    public ProceduralDiceRoller diceRoller;
    public AIBarkController barkController;
    public CampusGameLauncher launcher;
    public CampusTileInspector tileInspector;
    public CampusAudioManager audioManager;
    public OrbitalCamera orbitalCamera;

    // État interne synchronisé
    private bool initializedFromTurnManager = false;
    private float configuredDecisionDuration = 10f;
    private readonly Dictionary<int, int> knownTileLevels = new Dictionary<int, int>();

    // Messages & bannières
    private string activeSemesterEventTitle = "";
    private string activeSemesterEventEffect = "";
    private string lastCardBanner = "";
    private float cardBannerTimer = 0f;

    private string infoMessageText = "";
    private float infoMessageTimer = 0f;

    private string aiSpeechBubbleText = "";
    private float aiBubbleTimer = 0f;
    private float lastAISpeechTimestamp = -99f;
    private string lastAISpeechReceived = "";
    private readonly Queue<(string text, float duration)> aiSpeechQueue = new Queue<(string text, float duration)>();

    // Décision humaine
    private bool decisionTimerActive = false;
    private float decisionTimeRemaining = 10f;
    private string decisionPrompt = "";
    private bool isDetentionPrompt = false;

    // Modales
    private bool isGameOver = false;
    private bool isPaused = false;
    private bool isRulesOpen = false;
    private bool isNewRecord = false;
    private int gameOverWinnerId = 0;
    private int gameOverFinalScore = 0;
    private bool gameOverHumanWon = false;

    // Cache UI UGUI
    private Font uiFont;
    private Canvas rootCanvas;
    private GameObject mainMenuRoot;
    private GameObject inGameHudRoot;
    private GameObject pauseModalRoot;
    private GameObject rulesModalRoot;
    private GameObject gameOverModalRoot;
    private GameObject tileInspectorRoot;

    // Menu Principal UI
    private readonly Button[] menuLevelButtons = new Button[3];
    private readonly Image[] menuLevelBgs = new Image[3];
    private readonly Text[] menuLevelTexts = new Text[3];
    private readonly Button[] menuCharButtons = new Button[3];
    private readonly Image[] menuCharBgs = new Image[3];
    private readonly Text[] menuCharTexts = new Text[3];
    private Text menuSelectionSummaryText;
    private int menuSelectedLevel = 1;
    private int menuSelectedChar = 0;

    // HUD Haut-Gauche
    private Text hudHeaderText;
    private readonly Image[] playerCardBg = new Image[3];
    private readonly Image[] playerColorStripe = new Image[3];
    private readonly Image[] playerPortraitImg = new Image[3];
    private readonly Text[] playerNameText = new Text[3];
    private readonly Text[] playerFinanceText = new Text[3];
    private readonly Text[] playerStatusText = new Text[3];

    // HUD Haut-Centre
    private GameObject semesterEventBox;
    private Text semesterEventText;
    private GameObject cardToastBox;
    private Text cardToastText;

    // HUD Haut-Droite (barre d'outils)
    private Text btnAccelLabel;
    private Image btnAccelBg;
    private Text btnTurboLabel;
    private Image btnTurboBg;
    private Text btnMuteLabel;

    // HUD Bas-Centre
    private GameObject rollPromptPanel;
    private Text rollPromptText;
    private Button rollActionButton;
    private Text rollActionBtnText;

    private GameObject decisionPanel;
    private Text decisionPromptText;
    private Image decisionTimerFill;
    private GameObject yesNoGroup;
    private GameObject detentionGroup;
    private Button btnDetentionPay;
    private Text btnDetentionPayText;
    private Button btnDetentionTicket;

    private GameObject infoBannerBox;
    private Text infoBannerText;
    private GameObject aiBubbleBox;
    private Text aiBubbleText;

    // Inspecteur de case (Droite)
    private Image inspectorHeaderBg;
    private Text inspectorTitleText;
    private Text inspectorSubtitleText;
    private Text inspectorBodyText;

    // Fin de partie
    private Text gameOverTitleText;
    private Text gameOverSubtitleText;
    private Text gameOverLeaderboardText;
    private Button btnNextLevel;

    // Pause
    private Text pauseMuteBtnText;
    private Text pauseMusicBtnText;

    // Portraits procéduraux (fallback si CharacterData.Portrait == null)
    private readonly Dictionary<CharacterId, Sprite> proceduralPortraits = new Dictionary<CharacterId, Sprite>();

    // Palette sombre moderne (Campus Tycoon)
    private static readonly Color ColPanelDark = new Color(0.08f, 0.10f, 0.15f, 0.90f);
    private static readonly Color ColCardIdle = new Color(0.13f, 0.16f, 0.23f, 0.94f);
    private static readonly Color ColCardActive = new Color(0.18f, 0.27f, 0.42f, 0.98f);
    private static readonly Color ColAccentBlue = new Color(0.18f, 0.52f, 0.95f, 1f);
    private static readonly Color ColAccentGreen = new Color(0.16f, 0.72f, 0.44f, 1f);
    private static readonly Color ColAccentRed = new Color(0.84f, 0.24f, 0.26f, 1f);
    private static readonly Color ColAccentAmber = new Color(0.92f, 0.66f, 0.14f, 1f);
    private static readonly Color ColAccentPurple = new Color(0.54f, 0.28f, 0.86f, 1f);
    private static readonly Color ColTextMain = new Color(0.96f, 0.97f, 0.99f, 1f);
    private static readonly Color ColTextMuted = new Color(0.75f, 0.80f, 0.88f, 1f);

    void Awake()
    {
        ResolveFont();
        EnsureEventSystem();
        BuildProceduralPortraits();
        BuildCompleteUI();
    }

    void OnEnable()
    {
        bestScore = PlayerPrefs.GetInt("BestScore_Level2", 0);

        CampusEvents.OnTurnStarted += HandleTurnStarted;
        CampusEvents.OnDiceRolled += HandleDiceRolled;
        CampusEvents.OnPlayerMoved += HandlePlayerMoved;
        CampusEvents.OnMoneyChanged += HandleMoneyChanged;
        CampusEvents.OnPropertyBought += HandlePropertyBought;
        CampusEvents.OnPropertyUpgraded += HandlePropertyUpgraded;
        CampusEvents.OnRentPaid += HandleRentPaid;
        CampusEvents.OnCardDrawn += HandleCardDrawn;
        CampusEvents.OnGlobalEventStarted += HandleGlobalEventStarted;
        CampusEvents.OnPowerUsed += HandlePowerUsed;
        CampusEvents.OnDetentionChanged += HandleDetentionChanged;
        CampusEvents.OnPlayerBankrupt += HandlePlayerBankrupt;
        CampusEvents.OnGameOver += HandleGameOver;

        BindBarkControllerIfAvailable();
    }

    void Start()
    {
        if (turnManager == null) turnManager = FindFirstObjectByType<TurnManager>();
        if (board == null) board = FindFirstObjectByType<ProceduralBoardGenerator>();
        if (diceRoller == null) diceRoller = FindFirstObjectByType<ProceduralDiceRoller>();
        if (launcher == null) launcher = FindFirstObjectByType<CampusGameLauncher>();
        if (tileInspector == null) tileInspector = FindFirstObjectByType<CampusTileInspector>();
        if (audioManager == null) audioManager = FindFirstObjectByType<CampusAudioManager>();
        if (orbitalCamera == null) orbitalCamera = FindFirstObjectByType<OrbitalCamera>();

        if (audioManager == null && Application.isPlaying)
        {
            audioManager = gameObject.AddComponent<CampusAudioManager>();
        }
        if (tileInspector == null && Application.isPlaying)
        {
            tileInspector = gameObject.AddComponent<CampusTileInspector>();
        }
        if (tileInspector != null)
        {
            tileInspector.OnTileSelected -= HandleTileInspected;
            tileInspector.OnTileSelected += HandleTileInspected;
        }

        ReadConfiguredDecisionDuration();
        BindBarkControllerIfAvailable();

        if (launcher != null && launcher.IsInMainMenu)
        {
            menuSelectedLevel = launcher.SelectedLevelIndex;
            menuSelectedChar = launcher.SelectedCharacterIndex;
            ShowMainMenuView(true);
            RefreshMainMenuCards();
        }
        else
        {
            ShowMainMenuView(false);
            TrySyncWithTurnManager();
            ApplyAnimationSpeedForPlayer(currentPlayerTurn);
        }

        RefreshToolbarVisuals();
    }

    void OnDisable()
    {
        CampusEvents.OnTurnStarted -= HandleTurnStarted;
        CampusEvents.OnDiceRolled -= HandleDiceRolled;
        CampusEvents.OnPlayerMoved -= HandlePlayerMoved;
        CampusEvents.OnMoneyChanged -= HandleMoneyChanged;
        CampusEvents.OnPropertyBought -= HandlePropertyBought;
        CampusEvents.OnPropertyUpgraded -= HandlePropertyUpgraded;
        CampusEvents.OnRentPaid -= HandleRentPaid;
        CampusEvents.OnCardDrawn -= HandleCardDrawn;
        CampusEvents.OnGlobalEventStarted -= HandleGlobalEventStarted;
        CampusEvents.OnPowerUsed -= HandlePowerUsed;
        CampusEvents.OnDetentionChanged -= HandleDetentionChanged;
        CampusEvents.OnPlayerBankrupt -= HandlePlayerBankrupt;
        CampusEvents.OnGameOver -= HandleGameOver;

        if (tileInspector != null)
        {
            tileInspector.OnTileSelected -= HandleTileInspected;
        }
        if (barkController != null)
        {
            barkController.OnBark -= HandleBarkLineReceived;
        }
    }

    public void NotifyGameStartedFromLauncher()
    {
        initializedFromTurnManager = false;
        isGameOver = false;
        isPaused = false;
        isRulesOpen = false;
        knownTileLevels.Clear();
        activeSemesterEventTitle = "";
        activeSemesterEventEffect = "";
        ShowMainMenuView(false);
        ReadConfiguredDecisionDuration();
        TrySyncWithTurnManager();
        ApplyAnimationSpeedForPlayer(0);
    }

    /// <summary>
    /// Relie AIBarkController au HUD sans dupliquer l'événement persistant déjà configuré dans Scene_Main (fix P1-10).
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

        if (barkController != null)
        {
            barkController.OnBark -= HandleBarkLineReceived;
            barkController.OnBark += HandleBarkLineReceived;
        }
    }

    private void HandleBarkLineReceived(AIBarkLine line)
    {
        if (line != null)
        {
            ShowAISpeech(line.Formatted, 4.5f);
        }
    }

    private void ReadConfiguredDecisionDuration()
    {
        if (turnManager == null) return;
        FieldInfo f = turnManager.GetType().GetField("humanDecisionSeconds", BindingFlags.Instance | BindingFlags.NonPublic);
        if (f != null && f.GetValue(turnManager) is float secs && secs >= 1f)
        {
            configuredDecisionDuration = secs;
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
            studentNames[i] = $"{roleTag} · {p.Character.DisplayName}";
            studentMoney[i] = p.Money;
        }

        BindBarkControllerIfAvailable();
        if (barkController != null)
        {
            AIController[] rivals = FindObjectsByType<AIController>(FindObjectsSortMode.None);
            barkController.Bind(state.Board, rivals);
        }

        initializedFromTurnManager = true;
        RefreshAllInGamePanels();
    }

    void Update()
    {
        if (launcher != null && launcher.IsInMainMenu)
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && isRulesOpen)
            {
                SetRulesModalOpen(false);
            }
            return;
        }

        if (!initializedFromTurnManager)
        {
            TrySyncWithTurnManager();
        }

        // Gestion de la touche [Échap] : ferme inspecteur/règles ou bascule le menu Pause
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && !isGameOver)
        {
            if (isRulesOpen)
            {
                SetRulesModalOpen(false);
            }
            else if (tileInspector != null && tileInspector.SelectedTile != null)
            {
                tileInspector.ClearSelection();
            }
            else
            {
                SetPaused(!isPaused);
            }
        }

        // Raccourcis globaux hors Pause/GameOver : [V] Turbo, [M] Mute, [H] Aide
        if (!isPaused && !isGameOver && Keyboard.current != null)
        {
            if (Keyboard.current.vKey.wasPressedThisFrame)
            {
                ToggleTurboMode();
            }
            if (Keyboard.current.mKey.wasPressedThisFrame)
            {
                ToggleMute();
            }
            if (Keyboard.current.hKey.wasPressedThisFrame)
            {
                SetRulesModalOpen(!isRulesOpen);
            }
        }

        // Fix P1-1 : Efface automatiquement la bannière d'Événement du semestre dès que State.ActiveEvent redevient null
        if (turnManager != null && turnManager.State != null && turnManager.State.ActiveEvent == null &&
            !string.IsNullOrEmpty(activeSemesterEventTitle))
        {
            activeSemesterEventTitle = "";
            activeSemesterEventEffect = "";
        }

        float dt = Time.deltaTime;
        if (cardBannerTimer > 0f) cardBannerTimer -= dt;
        if (infoMessageTimer > 0f) infoMessageTimer -= dt;

        if (aiBubbleTimer > 0f)
        {
            aiBubbleTimer -= dt;
            if (aiBubbleTimer <= 0f && aiSpeechQueue.Count > 0)
            {
                var next = aiSpeechQueue.Dequeue();
                aiSpeechBubbleText = next.text;
                aiBubbleTimer = next.duration;
            }
        }

        if (decisionTimerActive)
        {
            decisionTimeRemaining -= dt;
            if (decisionTimeRemaining <= 0f)
            {
                decisionTimeRemaining = 0f;
                decisionTimerActive = false;
                ShowInfoMessage($"Temps écoulé ({configuredDecisionDuration:0} s) : décision automatique « Passer ».");
            }
        }

        RefreshAllInGamePanels();
    }

    private void ApplyAnimationSpeedForPlayer(int playerId)
    {
        float targetSpeed = 1f;
        float targetAIPause = normalAIPauseSeconds;

        if (turboModeActive)
        {
            targetSpeed = turboSpeedMultiplier;
            targetAIPause = turboAIPauseSeconds;
        }
        else if (accelerateAITurns)
        {
            if (playerId != 0) targetSpeed = aiTurnSpeedMultiplier;
            targetAIPause = fastAIPauseSeconds;
        }

        if (board == null) board = FindFirstObjectByType<ProceduralBoardGenerator>();
        if (board != null) board.SetAllPawnsSpeedMultiplier(targetSpeed);

        if (diceRoller == null) diceRoller = FindFirstObjectByType<ProceduralDiceRoller>();
        if (diceRoller != null) diceRoller.speedMultiplier = targetSpeed;

        // Adapte aussi le temps de réflexion IA sur TurnManager (sans modifier Core/TurnManager.cs)
        if (turnManager == null) turnManager = FindFirstObjectByType<TurnManager>();
        if (turnManager != null)
        {
            FieldInfo pauseField = turnManager.GetType().GetField("aiPause", BindingFlags.Instance | BindingFlags.NonPublic);
            if (pauseField != null)
            {
                pauseField.SetValue(turnManager, targetAIPause);
            }
        }
    }

    // ==================== API PUBLIQUE CONSERVÉE (COMPATIBILITÉ TURNMANAGER / MOVERTESTER) ====================

    public void StartHumanDecisionTimer(string promptText)
    {
        decisionPrompt = promptText ?? "";
        isDetentionPrompt = decisionPrompt.StartsWith("Colle", StringComparison.OrdinalIgnoreCase);
        decisionTimeRemaining = configuredDecisionDuration;
        decisionTimerActive = true;
    }

    public void StopHumanDecisionTimer()
    {
        decisionTimerActive = false;
        isDetentionPrompt = false;
    }

    public void ShowInfoMessage(string msg, float duration = 3.6f)
    {
        if (string.IsNullOrEmpty(msg)) return;
        infoMessageText = msg;
        infoMessageTimer = duration;
    }

    /// <summary>
    /// Affiche une réplique IA dans sa bulle dédiée, avec file d'attente et déduplication (évite double appel).
    /// </summary>
    public void ShowAISpeech(string msg, float duration = 4.5f)
    {
        if (string.IsNullOrEmpty(msg)) return;

        // Déduplication (si UnityEvent persistant + C# event appellent tous deux sur la même frame)
        if (msg == lastAISpeechReceived && (Time.unscaledTime - lastAISpeechTimestamp) < 0.15f)
        {
            return;
        }
        lastAISpeechReceived = msg;
        lastAISpeechTimestamp = Time.unscaledTime;

        if (aiBubbleTimer > 0.25f && aiSpeechBubbleText != msg)
        {
            aiSpeechQueue.Enqueue((msg, duration));
            return;
        }

        aiSpeechBubbleText = msg;
        aiBubbleTimer = duration;
    }

    // ==================== RÉACTIONS AUX ÉVÉNEMENTS OBSERVER ====================

    private void HandleTurnStarted(int playerId, int turn, int totalTurns)
    {
        TrySyncWithTurnManager();
        currentPlayerTurn = Mathf.Clamp(playerId, 0, 2);
        currentTurnNumber = turn;
        maxTurns = totalTurns;
        ApplyAnimationSpeedForPlayer(currentPlayerTurn);
    }

    private void HandleDiceRolled(int playerId, int d1, int d2, bool isDouble)
    {
        string who = ShortPlayerName(playerId);
        if (isDouble)
        {
            ShowInfoMessage($"DOUBLE ({d1} + {d2} = {d1 + d2}) pour {who} ! Rejeu accordé après résolution.", 3.4f);
        }
    }

    private void HandlePlayerMoved(int playerId, int targetTileIndex, int stepsDelta, bool sentToColle)
    {
        if (sentToColle)
        {
            ShowInfoMessage($"{ShortPlayerName(playerId)} est envoyé directement en Colle !", 3.5f);
        }
    }

    private void HandleMoneyChanged(int playerId, int newMoney, int delta)
    {
        if (playerId >= 0 && playerId < studentMoney.Length)
        {
            studentMoney[playerId] = newMoney;
        }

        if (turnManager != null && turnManager.State != null && playerId >= 0 && playerId < turnManager.State.Players.Count)
        {
            Player p = turnManager.State.Players[playerId];
            BoardSquare curSq = (p.Position >= 0 && p.Position < turnManager.State.Board.Count)
                ? turnManager.State.Board[p.Position]
                : null;

            // Retour HUD pour événements non émis séparément par TurnManager (Roulette, La Rentrée, Foyer, Taxe)
            if (curSq != null)
            {
                if (curSq.Data.Type == SquareType.Roulette && delta > 0)
                {
                    ShowInfoMessage($"ROULETTE DU BDE : {ShortPlayerName(playerId)} remporte {delta} € !", 4.0f);
                }
                else if (curSq.Data.Type == SquareType.StudentLounge && delta == curSq.Data.Amount)
                {
                    ShowInfoMessage($"Foyer étudiant : {ShortPlayerName(playerId)} gagne +{delta} €.", 3.0f);
                }
                else if (curSq.Data.Type == SquareType.Tax && delta == -curSq.Data.Amount)
                {
                    ShowInfoMessage($"Frais de scolarité : {ShortPlayerName(playerId)} paie {curSq.Data.Amount} €.", 3.0f);
                }
            }

            if (delta == turnManager.State.Level.PassStartBonus && turnManager.Phase == TurnManager.TurnPhase.Move)
            {
                ShowInfoMessage($"Passage par La Rentrée : {ShortPlayerName(playerId)} touche +{delta} € !", 3.2f);
            }
        }
    }

    private void HandlePropertyBought(int playerId, int tileIndex, int price)
    {
        StopHumanDecisionTimer();
        knownTileLevels[tileIndex] = 0;
        string tileLabel = GetSquareName(tileIndex);
        ShowInfoMessage($"{ShortPlayerName(playerId)} achète {tileLabel} pour {price} €.", 3.5f);
    }

    private void HandlePropertyUpgraded(int playerId, int tileIndex, int newLevel)
    {
        StopHumanDecisionTimer();
        int prevLevel = knownTileLevels.TryGetValue(tileIndex, out int oldLvl) ? oldLvl : 0;
        knownTileLevels[tileIndex] = newLevel;
        string tileLabel = GetSquareName(tileIndex);

        // Fix P1-2 : Distingue une vraie amélioration d'une vente forcée d'étage !
        if (newLevel > prevLevel)
        {
            ShowInfoMessage($"{ShortPlayerName(playerId)} améliore {tileLabel} au Niveau {newLevel} !", 3.5f);
        }
        else if (newLevel < prevLevel)
        {
            ShowInfoMessage($"Vente forcée : {ShortPlayerName(playerId)} vend un étage de {tileLabel} (Niv. {newLevel}).", 3.8f);
        }
    }

    private void HandleRentPaid(int tenantId, int ownerId, int tileIndex, int amount)
    {
        string tileLabel = GetSquareName(tileIndex);
        ShowInfoMessage($"Loyer ({tileLabel}) : {ShortPlayerName(tenantId)} paie {amount} € à {ShortPlayerName(ownerId)}.", 3.8f);
    }

    private void HandleCardDrawn(int playerId, int cardId, string title, string effect)
    {
        lastCardBanner = $"CARTE ÉVÉNEMENT ({ShortPlayerName(playerId)}) — {title} : {effect}";
        cardBannerTimer = 5.5f;
    }

    private void HandleGlobalEventStarted(string title, string effect)
    {
        activeSemesterEventTitle = title ?? "";
        activeSemesterEventEffect = effect ?? "";
    }

    private void HandlePowerUsed(int playerId, string charName, string powerName, string desc)
    {
        ShowInfoMessage($"POUVOIR [{charName}] — {powerName} : {desc}", 4.2f);
    }

    private void HandleDetentionChanged(int playerId, bool inDetention)
    {
        if (inDetention)
        {
            ShowInfoMessage($"{ShortPlayerName(playerId)} passe en Colle.", 3.2f);
        }
    }

    private void HandlePlayerBankrupt(int playerId, int creditorId)
    {
        StopHumanDecisionTimer();
        ShowInfoMessage($"FAILLITE : {ShortPlayerName(playerId)} est éliminé ! Ses terrains retournent à la banque.", 4.5f);
    }

    private void HandleGameOver(int winnerId, int finalScore, bool isHumanVictory)
    {
        isGameOver = true;
        isPaused = false;
        Time.timeScale = 1f;
        gameOverWinnerId = Mathf.Clamp(winnerId, 0, 2);
        gameOverFinalScore = finalScore;
        gameOverHumanWon = isHumanVictory;

        int levelNum = (turnManager != null && turnManager.State != null && turnManager.State.Level != null)
            ? turnManager.State.Level.LevelNumber
            : 2;
        string key = $"BestScore_Level{levelNum}";
        int savedBest = PlayerPrefs.GetInt(key, 0);

        isNewRecord = isHumanVictory && finalScore >= savedBest && finalScore > 0;
        if (isHumanVictory && finalScore > bestScore)
        {
            bestScore = finalScore;
            PlayerPrefs.SetInt(key, bestScore);
            PlayerPrefs.Save();
        }
        else
        {
            bestScore = Mathf.Max(bestScore, savedBest);
        }

        PopulateGameOverScreen();
    }

    // ==================== ACTIONS DES BOUTONS UI & SYNTHÈSE CLAVIER ====================

    private void TriggerVirtualKey(Key key)
    {
        ClearUISelection();
        PlayClickSound();
        StartCoroutine(SimulateKeyTapCoroutine(key));
    }

    private IEnumerator SimulateKeyTapCoroutine(Key key)
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) yield break;
        InputSystem.QueueStateEvent(kb, new KeyboardState(key));
        yield return null;
        InputSystem.QueueStateEvent(kb, new KeyboardState());
    }

    private void ClearUISelection()
    {
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    private void PlayClickSound()
    {
        if (audioManager != null) audioManager.PlayUIClick();
    }

    private void ToggleAccelerateAI()
    {
        ClearUISelection();
        PlayClickSound();
        accelerateAITurns = !accelerateAITurns;
        ApplyAnimationSpeedForPlayer(currentPlayerTurn);
        RefreshToolbarVisuals();
    }

    private void ToggleTurboMode()
    {
        ClearUISelection();
        PlayClickSound();
        turboModeActive = !turboModeActive;
        ApplyAnimationSpeedForPlayer(currentPlayerTurn);
        RefreshToolbarVisuals();
    }

    private void ToggleMute()
    {
        ClearUISelection();
        if (audioManager != null) audioManager.ToggleMute();
        RefreshToolbarVisuals();
    }

    private void ToggleMusic()
    {
        ClearUISelection();
        if (audioManager != null) audioManager.ToggleMusic();
        RefreshToolbarVisuals();
    }

    private void RecenterCamera()
    {
        ClearUISelection();
        PlayClickSound();
        if (orbitalCamera == null) orbitalCamera = FindFirstObjectByType<OrbitalCamera>();
        if (orbitalCamera != null) orbitalCamera.ResetView();
    }

    private void SetPaused(bool paused)
    {
        ClearUISelection();
        PlayClickSound();
        isPaused = paused;
        Time.timeScale = isPaused ? 0f : 1f;
        if (pauseModalRoot != null) pauseModalRoot.SetActive(isPaused);
        RefreshToolbarVisuals();
    }

    private void SetRulesModalOpen(bool open)
    {
        ClearUISelection();
        PlayClickSound();
        isRulesOpen = open;
        if (rulesModalRoot != null) rulesModalRoot.SetActive(isRulesOpen);
    }

    // ==================== INSPECTEUR DE CASE (CARTE DE PROPRIÉTÉ) ====================

    private void HandleTileInspected(ProceduralTileView tile, BoardSquare liveSquare, SquareData data)
    {
        if (tileInspectorRoot == null) return;
        if (tile == null || data == null)
        {
            tileInspectorRoot.SetActive(false);
            return;
        }

        tileInspectorRoot.SetActive(true);
        inspectorTitleText.text = $"[{tile.tileIndex}] {data.DisplayName}";

        Color headerColor = tile.category == ProceduralTileView.TileCategory.Property
            ? ProceduralBuildingGenerator.GetColorForCampusGroup(tile.propertyGroup)
            : (tile.category == ProceduralTileView.TileCategory.Station ? new Color(0.20f, 0.28f, 0.40f)
            : (tile.category == ProceduralTileView.TileCategory.Company ? new Color(0.45f, 0.28f, 0.16f)
            : (tile.category == ProceduralTileView.TileCategory.Special ? ColAccentAmber : ColAccentBlue)));
        inspectorHeaderBg.color = headerColor;

        GameState state = turnManager != null ? turnManager.State : null;
        string ownerLabel = "Libre à l'achat";
        if (liveSquare != null && liveSquare.IsOwned && state != null && liveSquare.OwnerIndex < state.Players.Count)
        {
            Player owner = state.Players[liveSquare.OwnerIndex];
            string role = owner.IsHuman ? "Vous" : $"IA {owner.Index}";
            ownerLabel = $"Propriétaire : {role} ({owner.Character.DisplayName})";
        }
        else if (!data.IsOwnable)
        {
            ownerLabel = "Case spéciale du campus";
        }

        inspectorSubtitleText.text = ownerLabel;

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        bool saleActive = state != null && state.ActiveEvent == GlobalEventType.BackToSchoolSale;
        bool openDay = state != null && state.ActiveEvent == GlobalEventType.OpenDay;
        bool strike = state != null && state.ActiveEvent == GlobalEventType.TransportStrike;

        if (data.Type == SquareType.Property)
        {
            int curLvl = liveSquare != null ? liveSquare.UpgradeLevel : 0;
            bool hasMono = liveSquare != null && liveSquare.IsOwned && state != null && state.HasMonopoly(liveSquare.OwnerIndex, data.Group);
            int upgCost = saleActive ? data.UpgradeCost / 2 : data.UpgradeCost;

            sb.AppendLine($"Prix d'achat : {data.Price} €");
            sb.AppendLine(saleActive
                ? $"Coût d'un étage : {upgCost} € (SOLDES -50 % !)"
                : $"Coût d'un étage : {upgCost} € (revente : {data.UpgradeCost / 2} €)");
            sb.AppendLine($"Niveau actuel : {curLvl} / 3" + (hasMono ? "  [MONOPOLE]" : ""));
            sb.AppendLine("--------------------------------");
            sb.AppendLine("Grille des loyers" + (openDay ? " (x2 PORTES OUVERTES !)" : "") + " :");
            int mult = openDay ? 2 : 1;
            if (data.Rents.Count >= 5)
            {
                sb.AppendLine($"{(curLvl == 0 && !hasMono && liveSquare != null && liveSquare.IsOwned ? "> " : "  ")}Terrain nu : {data.Rents[0] * mult} €");
                sb.AppendLine($"{(curLvl == 0 && hasMono ? "> " : "  ")}Monopole (2R) : {data.Rents[1] * mult} €");
                sb.AppendLine($"{(curLvl == 1 ? "> " : "  ")}Niveau 1 (5R) : {data.Rents[2] * mult} €");
                sb.AppendLine($"{(curLvl == 2 ? "> " : "  ")}Niveau 2 (12R) : {data.Rents[3] * mult} €");
                sb.AppendLine($"{(curLvl == 3 ? "> " : "  ")}Niveau 3 (22R) : {data.Rents[4] * mult} €");
            }
        }
        else if (data.Type == SquareType.Station)
        {
            sb.AppendLine($"Prix d'achat : {data.Price} €");
            sb.AppendLine("--------------------------------");
            if (strike) sb.AppendLine("GRÈVE DU RER ACTIVE : loyer = 0 € !");
            if (data.Rents.Count >= 2)
            {
                sb.AppendLine($"  1 gare possédée : {data.Rents[0]} €");
                sb.AppendLine($"  2 gares possédées : {data.Rents[1]} €");
            }
        }
        else if (data.Type == SquareType.Utility)
        {
            sb.AppendLine($"Prix d'achat : {data.Price} €");
            sb.AppendLine("--------------------------------");
            int factor = data.Rents.Count > 0 ? data.Rents[0] : 5;
            sb.AppendLine($"Loyer = {factor} x somme des dés du visiteur.");
            sb.AppendLine($"(Moyenne attendue : ~{factor * 7} €)");
        }
        else
        {
            switch (data.Type)
            {
                case SquareType.Start:
                    sb.AppendLine("Franchir ou s'arrêter ici en avançant rapporte +150 €.");
                    break;
                case SquareType.Detention:
                    sb.AppendLine("Simple visite si vous y tombez normalement.");
                    sb.AppendLine("En colle : payez 50 €, utilisez un Billet d'absence, ou passez 1 tour.");
                    break;
                case SquareType.StudentLounge:
                    sb.AppendLine($"Pause au foyer : recevez +{data.Amount} € !");
                    break;
                case SquareType.GoToDetention:
                    sb.AppendLine("Envoie directement en Colle sans toucher le bonus de La Rentrée.");
                    break;
                case SquareType.Tax:
                    sb.AppendLine($"Payez {data.Amount} € à la banque (annulable par le pouvoir du Fêtard).");
                    break;
                case SquareType.EventCard:
                    sb.AppendLine("Piochez une carte Événement parmi les 12 cartes du campus.");
                    break;
                case SquareType.Roulette:
                    sb.AppendLine($"Mise facultative : {data.Amount} € (1 seul dé).");
                    sb.AppendLine("  1 ou 2 : 0 €   |   3 ou 4 : 100 €   |   5 ou 6 : 200 €");
                    break;
            }
        }

        inspectorBodyText.text = sb.ToString().TrimEnd();
    }

    // ==================== RAFRAÎCHISSEMENT VISUEL ====================

    private void ShowMainMenuView(bool showMenu)
    {
        if (mainMenuRoot != null) mainMenuRoot.SetActive(showMenu);
        if (inGameHudRoot != null) inGameHudRoot.SetActive(!showMenu);
        if (pauseModalRoot != null) pauseModalRoot.SetActive(false);
        if (gameOverModalRoot != null) gameOverModalRoot.SetActive(false);
        if (tileInspectorRoot != null) tileInspectorRoot.SetActive(false);
    }

    private void RefreshMainMenuCards()
    {
        if (launcher == null) return;

        for (int i = 0; i < 3; i++)
        {
            LevelData lvl = launcher.GetLevel(i);
            bool sel = (i == menuSelectedLevel);
            if (menuLevelBgs[i] != null)
            {
                menuLevelBgs[i].color = sel ? ColAccentBlue : ColCardIdle;
            }
            if (menuLevelTexts[i] != null && lvl != null)
            {
                int rec = PlayerPrefs.GetInt($"BestScore_Level{lvl.LevelNumber}", 0);
                string diffLabel = lvl.Difficulty == AIDifficulty.Easy ? "Facile"
                    : (lvl.Difficulty == AIDifficulty.Normal ? "Normale" : "Difficile");
                string extra = lvl.LevelNumber == 3 ? " · Roulette BDE" : (lvl.GlobalEventTurns.Count > 0 ? $" · {lvl.GlobalEventTurns.Count} évén." : " · Sans évén.");
                menuLevelTexts[i].text =
                    $"NIVEAU {lvl.LevelNumber} — {lvl.LevelName}\n" +
                    $"{lvl.SquareCount} cases · {lvl.StartingMoney} € · {lvl.MaxTurns} tours · IA {diffLabel}{extra}\n" +
                    $"Meilleur score : {(rec > 0 ? rec + " pts" : "Aucun")}";
            }
        }

        for (int i = 0; i < 3; i++)
        {
            CharacterData ch = launcher.GetCharacter(i);
            bool sel = (i == menuSelectedChar);
            if (menuCharBgs[i] != null)
            {
                menuCharBgs[i].color = sel ? (ch != null ? Color.Lerp(ch.TokenColor, Color.black, 0.25f) : ColAccentGreen) : ColCardIdle;
            }
            if (menuCharTexts[i] != null && ch != null)
            {
                menuCharTexts[i].text =
                    $"{ch.DisplayName}\n" +
                    $"Pouvoir : {ch.PowerName}\n" +
                    $"{ch.PowerDescription}";
            }
        }

        if (menuSelectionSummaryText != null)
        {
            LevelData lvl = launcher.GetLevel(menuSelectedLevel);
            CharacterData ch = launcher.GetCharacter(menuSelectedChar);
            if (lvl != null && ch != null)
            {
                menuSelectionSummaryText.text = $"Prêt : Niveau {lvl.LevelNumber} ({lvl.LevelName}) avec {ch.DisplayName} contre 2 rivaux IA";
            }
        }
    }

    private void RefreshToolbarVisuals()
    {
        if (btnAccelLabel != null)
        {
            btnAccelLabel.text = accelerateAITurns ? $"IA x{aiTurnSpeedMultiplier:0.0} : ON" : "IA Rapide : OFF";
            btnAccelBg.color = accelerateAITurns ? new Color(0.16f, 0.45f, 0.72f, 0.95f) : ColCardIdle;
        }
        if (btnTurboLabel != null)
        {
            btnTurboLabel.text = turboModeActive ? $"Turbo [V] : ON" : "Turbo [V] : OFF";
            btnTurboBg.color = turboModeActive ? ColAccentAmber : ColCardIdle;
        }
        bool muted = audioManager != null && audioManager.isMuted;
        bool musicMuted = audioManager != null && audioManager.isMusicMuted;
        if (btnMuteLabel != null) btnMuteLabel.text = muted ? "Son [M] : OFF" : "Son [M] : ON";
        if (pauseMuteBtnText != null) pauseMuteBtnText.text = muted ? "Activer tout le son" : "Couper tout le son";
        if (pauseMusicBtnText != null) pauseMusicBtnText.text = musicMuted ? "Musique calme : OFF" : "Musique calme : ON";
    }

    private void RefreshAllInGamePanels()
    {
        if (inGameHudRoot == null || !inGameHudRoot.activeSelf) return;

        GameState state = turnManager != null ? turnManager.State : null;
        string levelTag = (state != null && state.Level != null)
            ? $"Niv. {state.Level.LevelNumber} ({state.Level.LevelName})"
            : "Mode Démo";

        if (hudHeaderText != null)
        {
            hudHeaderText.text = $"CAMPUS TYCOON  ·  {levelTag}\nTour {currentTurnNumber} / {maxTurns}   |   Record : {bestScore}";
        }

        for (int i = 0; i < 3; i++)
        {
            bool isTurn = (i == currentPlayerTurn);
            Color pCol = board != null ? board.GetPlayerColor(i) : ProceduralBoardGenerator.StudentPawnColors[i];

            if (playerColorStripe[i] != null) playerColorStripe[i].color = pCol;
            if (playerCardBg[i] != null)
            {
                bool bankrupt = state != null && i < state.Players.Count && state.Players[i].IsBankrupt;
                playerCardBg[i].color = bankrupt
                    ? new Color(0.18f, 0.10f, 0.12f, 0.78f)
                    : (isTurn ? ColCardActive : ColCardIdle);
            }

            if (state != null && i < state.Players.Count)
            {
                Player p = state.Players[i];
                if (playerPortraitImg[i] != null)
                {
                    Sprite sp = p.Character.Portrait != null
                        ? p.Character.Portrait
                        : (proceduralPortraits.TryGetValue(p.Character.Id, out Sprite proc) ? proc : null);
                    playerPortraitImg[i].sprite = sp;
                    playerPortraitImg[i].color = p.Character.Portrait != null ? Color.white : pCol;
                }

                string turnPrefix = isTurn && !p.IsBankrupt ? "> " : "";
                playerNameText[i].text = $"{turnPrefix}{(p.IsHuman ? "VOUS" : $"IA {i}")} · {p.Character.DisplayName}";

                int worth = state.NetWorth(i);
                int ownedCount = state.OwnedBy(i).Count();
                playerFinanceText[i].text = p.IsBankrupt
                    ? "ÉLIMINÉ (Faillite)"
                    : $"{p.Money} €   |   Fortune : {worth} €   |   {ownedCount} bien{(ownedCount > 1 ? "s" : "")}";

                List<string> badges = new List<string>();
                if (p.IsBankrupt)
                {
                    badges.Add("[FAILLITE]");
                }
                else
                {
                    if (isTurn && !p.IsHuman)
                    {
                        string phaseLabel = turnManager != null && turnManager.Phase == TurnManager.TurnPhase.Move
                            ? "Se déplace..."
                            : "Réfléchit...";
                        badges.Add(phaseLabel);
                    }
                    badges.Add(p.PowerUsed ? "Pouvoir utilisé" : $"Pouvoir : {p.Character.PowerName}");
                    if (p.HasExemptionTicket) badges.Add("[BILLET]");
                    if (p.InDetention) badges.Add("[EN COLLE]");
                    if (p.MustSkipNextTurn) badges.Add("[TOUR PASSÉ]");
                }
                playerStatusText[i].text = string.Join("  ·  ", badges);
                playerStatusText[i].color = p.InDetention ? ColAccentRed : (p.HasExemptionTicket ? ColAccentAmber : ColTextMuted);
            }
            else
            {
                playerNameText[i].text = (isTurn ? "> " : "") + studentNames[i];
                playerFinanceText[i].text = $"{studentMoney[i]} €";
                playerStatusText[i].text = "Mode Bac à Sable";
            }
        }

        // Bannière Événement du semestre
        bool hasSemesterEvent = !string.IsNullOrEmpty(activeSemesterEventTitle);
        if (semesterEventBox != null) semesterEventBox.SetActive(hasSemesterEvent);
        if (hasSemesterEvent && semesterEventText != null)
        {
            semesterEventText.text = $"ÉVÉNEMENT DU SEMESTRE — {activeSemesterEventTitle} : {activeSemesterEventEffect}";
        }

        // Pop-up Carte Événement
        bool hasCard = cardBannerTimer > 0f && !string.IsNullOrEmpty(lastCardBanner);
        if (cardToastBox != null) cardToastBox.SetActive(hasCard);
        if (hasCard && cardToastText != null) cardToastText.text = lastCardBanner;

        // Zone 1 : Info système
        bool hasInfo = infoMessageTimer > 0f && !string.IsNullOrEmpty(infoMessageText);
        if (infoBannerBox != null) infoBannerBox.SetActive(hasInfo);
        if (hasInfo && infoBannerText != null) infoBannerText.text = infoMessageText;

        // Zone 2 : Bulle IA
        bool hasAI = aiBubbleTimer > 0f && !string.IsNullOrEmpty(aiSpeechBubbleText);
        if (aiBubbleBox != null) aiBubbleBox.SetActive(hasAI);
        if (hasAI && aiBubbleText != null) aiBubbleText.text = aiSpeechBubbleText;

        // Fix P1-5 : Invitation Lancer les dés (masquée pendant le lancer de dés, affiche "Passer animation" si en vol)
        bool isRolling = diceRoller != null && diceRoller.IsRolling;
        bool waitingRoll = !isGameOver && !isPaused && turnManager != null &&
                           turnManager.Phase == TurnManager.TurnPhase.Roll &&
                           currentPlayerTurn == 0 && !decisionTimerActive;

        if (rollPromptPanel != null)
        {
            rollPromptPanel.SetActive(waitingRoll);
            if (waitingRoll)
            {
                if (isRolling)
                {
                    rollPromptText.text = "Lancer en cours… Cliquez pour passer l'animation";
                    rollActionBtnText.text = "Passer animation";
                }
                else
                {
                    rollPromptText.text = "C'est votre tour ! Lancez les dés pour avancer.";
                    rollActionBtnText.text = "Lancer les dés [Espace]";
                }
            }
        }

        // Panneau Décision Humaine avec boutons cliquables
        bool showDecision = !isGameOver && decisionTimerActive;
        if (decisionPanel != null)
        {
            decisionPanel.SetActive(showDecision);
            if (showDecision)
            {
                decisionPromptText.text = $"{decisionPrompt}   ({decisionTimeRemaining:0.0} s)";
                if (decisionTimerFill != null)
                {
                    float ratio = Mathf.Clamp01(decisionTimeRemaining / Mathf.Max(1f, configuredDecisionDuration));
                    decisionTimerFill.rectTransform.anchorMax = new Vector2(ratio, 1f);
                    decisionTimerFill.color = decisionTimeRemaining <= 3f ? ColAccentRed : ColAccentBlue;
                }

                if (yesNoGroup != null) yesNoGroup.SetActive(!isDetentionPrompt);
                if (detentionGroup != null) detentionGroup.SetActive(isDetentionPrompt);

                if (isDetentionPrompt && state != null && state.Players.Count > 0)
                {
                    Player human = state.Players[0];
                    int fine = state.Level != null ? state.Level.DetentionFine : 50;
                    if (btnDetentionPay != null)
                    {
                        btnDetentionPay.interactable = human.CanAfford(fine);
                        btnDetentionPayText.text = $"Payer {fine} € [P]";
                    }
                    if (btnDetentionTicket != null)
                    {
                        btnDetentionTicket.interactable = human.HasExemptionTicket;
                    }
                }
            }
        }
    }

    private void PopulateGameOverScreen()
    {
        if (gameOverModalRoot == null) return;
        gameOverModalRoot.SetActive(true);
        if (tileInspector != null) tileInspector.ClearSelection();

        gameOverTitleText.text = gameOverHumanWon
            ? "VICTOIRE ! VOUS DÉCROCHEZ LA BOURSE D'EXCELLENCE"
            : "FIN DU SEMESTRE — CLASSEMENT FINAL";
        gameOverTitleText.color = gameOverHumanWon ? ColAccentGreen : ColAccentAmber;

        string recordTag = isNewRecord ? "   [NOUVEAU RECORD !]" : $"   (Record du niveau : {bestScore})";
        gameOverSubtitleText.text = $"Vainqueur : {ShortPlayerName(gameOverWinnerId)}  ·  Score final : {gameOverFinalScore}{recordTag}";

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        GameState state = turnManager != null ? turnManager.State : null;
        if (state != null)
        {
            var ranked = state.Players
                .OrderBy(p => p.IsBankrupt ? 1 : 0)
                .ThenByDescending(p => state.NetWorth(p.Index))
                .ThenByDescending(p => state.OwnedBy(p.Index).Count())
                .ThenByDescending(p => p.Money)
                .ToList();

            int rank = 1;
            foreach (Player p in ranked)
            {
                int cash = p.Money;
                int propVal = 0;
                int upgVal = 0;
                foreach (BoardSquare sq in state.OwnedBy(p.Index))
                {
                    propVal += sq.Data.Price;
                    upgVal += sq.UpgradeLevel * sq.Data.UpgradeCost;
                }
                int total = cash + propVal + upgVal;
                string who = p.IsHuman ? $"VOUS ({p.Character.DisplayName})" : $"IA {p.Index} ({p.Character.DisplayName})";
                string status = p.IsBankrupt ? "  [ÉLIMINÉ]" : "";
                sb.AppendLine($"#{rank}  {who}{status}");
                sb.AppendLine($"     Fortune : {total} €   (Cash : {cash} €  +  Terrains : {propVal} €  +  Étages : {upgVal} €)");
                rank++;
            }
        }
        else
        {
            sb.AppendLine($"Gagnant : {ShortPlayerName(gameOverWinnerId)} — Score : {gameOverFinalScore}");
        }

        gameOverLeaderboardText.text = sb.ToString().TrimEnd();

        if (btnNextLevel != null)
        {
            btnNextLevel.gameObject.SetActive(launcher != null && launcher.HasNextLevel());
        }
    }

    private string ShortPlayerName(int playerId)
    {
        if (turnManager != null && turnManager.State != null && playerId >= 0 && playerId < turnManager.State.Players.Count)
        {
            Player p = turnManager.State.Players[playerId];
            return p.IsHuman ? "Vous" : p.Character.DisplayName;
        }
        return (playerId >= 0 && playerId < studentNames.Length) ? studentNames[playerId] : $"Joueur {playerId}";
    }

    private string GetSquareName(int tileIndex)
    {
        if (turnManager != null && turnManager.State != null && tileIndex >= 0 && tileIndex < turnManager.State.Board.Count)
        {
            return turnManager.State.Board[tileIndex].Data.DisplayName;
        }
        if (board != null && tileIndex >= 0 && tileIndex < board.generatedTiles.Count)
        {
            return board.generatedTiles[tileIndex].tileName;
        }
        return $"Case {tileIndex}";
    }

    // ==================== CONSTRUCTION DE L'INTERFACE UGUI RESPONSIVE ====================

    private void ResolveFont()
    {
        uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (uiFont == null)
        {
            uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Arial", "Liberation Sans" }, 16);
        }
    }

    private void EnsureEventSystem()
    {
        EventSystem existing = FindFirstObjectByType<EventSystem>();
        if (existing == null)
        {
            GameObject esGo = new GameObject("CampusEventSystem");
            existing = esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();
        }
        else if (existing.GetComponent<InputSystemUIInputModule>() == null)
        {
            StandaloneInputModule legacy = existing.GetComponent<StandaloneInputModule>();
            if (legacy != null) Destroy(legacy);
            existing.gameObject.AddComponent<InputSystemUIInputModule>();
        }
    }

    private void BuildProceduralPortraits()
    {
        proceduralPortraits[CharacterId.Major] = CreateAvatarSprite(CharacterId.Major);
        proceduralPortraits[CharacterId.Partygoer] = CreateAvatarSprite(CharacterId.Partygoer);
        proceduralPortraits[CharacterId.Techie] = CreateAvatarSprite(CharacterId.Techie);
    }

    private static Sprite CreateAvatarSprite(CharacterId id)
    {
        const int sz = 48;
        Texture2D tex = new Texture2D(sz, sz, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Color clear = new Color(0, 0, 0, 0);
        Color white = Color.white;
        Color light = new Color(1f, 1f, 1f, 0.30f);

        float cx = sz * 0.5f;
        float cy = sz * 0.5f;
        float r = sz * 0.44f;

        for (int y = 0; y < sz; y++)
        {
            for (int x = 0; x < sz; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist > r)
                {
                    tex.SetPixel(x, y, clear);
                    continue;
                }

                // Bordure circulaire blanche + symbole géométrique distinct par personnage
                bool isBorder = dist > r - 2.5f;
                bool isIcon = false;
                if (id == CharacterId.Major)
                {
                    // Losange (toque de diplômé)
                    isIcon = (Mathf.Abs(dx) + Mathf.Abs(dy - 2f)) < 11f || (Mathf.Abs(dx) < 7f && dy > -10f && dy < -3f);
                }
                else if (id == CharacterId.Partygoer)
                {
                    // Étoile / lunettes de fête
                    isIcon = (Mathf.Abs(dx) < 12f && Mathf.Abs(dy) < 3.5f) || (Mathf.Abs(dy) < 12f && Mathf.Abs(dx) < 3.5f);
                }
                else
                {
                    // Puce / engrenage carré du Techos
                    isIcon = (Mathf.Abs(dx) < 9f && Mathf.Abs(dy) < 9f) && !(Mathf.Abs(dx) < 4f && Mathf.Abs(dy) < 4f);
                }

                tex.SetPixel(x, y, (isBorder || isIcon) ? white : light);
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, sz, sz), new Vector2(0.5f, 0.5f));
    }

    private void BuildCompleteUI()
    {
        GameObject canvasGo = new GameObject("CampusTycoon_UI_Canvas");
        canvasGo.transform.SetParent(transform, false);

        rootCanvas = canvasGo.AddComponent<Canvas>();
        rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        rootCanvas.sortingOrder = 50;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        BuildInGameHUD(canvasGo.transform);
        BuildTileInspectorPanel(canvasGo.transform);
        BuildMainMenuOverlay(canvasGo.transform);
        BuildPauseModal(canvasGo.transform);
        BuildGameOverModal(canvasGo.transform);
        BuildRulesModal(canvasGo.transform);
    }

    private void BuildInGameHUD(Transform parent)
    {
        inGameHudRoot = CreateUIObject("InGameHUD", parent);
        StretchFull(inGameHudRoot.GetComponent<RectTransform>());

        // 1. Panneau Haut-Gauche : En-tête + 3 Cartes Joueurs
        GameObject leftPanel = CreatePanel("TopLeft_PlayersPanel", inGameHudRoot.transform, ColPanelDark);
        RectTransform leftRt = leftPanel.GetComponent<RectTransform>();
        leftRt.anchorMin = new Vector2(0f, 1f);
        leftRt.anchorMax = new Vector2(0f, 1f);
        leftRt.pivot = new Vector2(0f, 1f);
        leftRt.anchoredPosition = new Vector2(18f, -18f);
        leftRt.sizeDelta = new Vector2(430f, 342f);

        hudHeaderText = CreateText("Header", leftPanel.transform, "CAMPUS TYCOON", 18, FontStyle.Bold, ColTextMain, TextAnchor.MiddleLeft);
        RectTransform hdrRt = hudHeaderText.GetComponent<RectTransform>();
        hdrRt.anchorMin = new Vector2(0f, 1f);
        hdrRt.anchorMax = new Vector2(1f, 1f);
        hdrRt.pivot = new Vector2(0.5f, 1f);
        hdrRt.anchoredPosition = new Vector2(0f, -8f);
        hdrRt.sizeDelta = new Vector2(-24f, 48f);

        for (int i = 0; i < 3; i++)
        {
            GameObject card = CreatePanel($"PlayerCard_{i}", leftPanel.transform, ColCardIdle);
            RectTransform cRt = card.GetComponent<RectTransform>();
            cRt.anchorMin = new Vector2(0f, 1f);
            cRt.anchorMax = new Vector2(1f, 1f);
            cRt.pivot = new Vector2(0.5f, 1f);
            cRt.anchoredPosition = new Vector2(0f, -64f - i * 90f);
            cRt.sizeDelta = new Vector2(-20f, 82f);
            playerCardBg[i] = card.GetComponent<Image>();

            // Bande couleur joueur à gauche
            GameObject stripe = CreatePanel("ColorStripe", card.transform, ProceduralBoardGenerator.StudentPawnColors[i]);
            RectTransform sRt = stripe.GetComponent<RectTransform>();
            sRt.anchorMin = new Vector2(0f, 0f);
            sRt.anchorMax = new Vector2(0f, 1f);
            sRt.pivot = new Vector2(0f, 0.5f);
            sRt.anchoredPosition = Vector2.zero;
            sRt.sizeDelta = new Vector2(8f, 0f);
            playerColorStripe[i] = stripe.GetComponent<Image>();

            // Badge Portrait
            GameObject pObj = CreateUIObject("Portrait", card.transform);
            Image pImg = pObj.AddComponent<Image>();
            RectTransform pRt = pObj.GetComponent<RectTransform>();
            pRt.anchorMin = new Vector2(0f, 0.5f);
            pRt.anchorMax = new Vector2(0f, 0.5f);
            pRt.pivot = new Vector2(0f, 0.5f);
            pRt.anchoredPosition = new Vector2(16f, 0f);
            pRt.sizeDelta = new Vector2(44f, 44f);
            playerPortraitImg[i] = pImg;

            playerNameText[i] = CreateText("Name", card.transform, studentNames[i], 16, FontStyle.Bold, ColTextMain, TextAnchor.MiddleLeft);
            SetAnchoredRect(playerNameText[i].rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(70f, -6f), new Vector2(-80f, 24f));

            playerFinanceText[i] = CreateText("Finance", card.transform, "1000 €", 14, FontStyle.Normal, new Color(0.75f, 0.96f, 0.82f), TextAnchor.MiddleLeft);
            SetAnchoredRect(playerFinanceText[i].rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(70f, -31f), new Vector2(-80f, 22f));

            playerStatusText[i] = CreateText("Status", card.transform, "", 13, FontStyle.Italic, ColTextMuted, TextAnchor.MiddleLeft);
            SetAnchoredRect(playerStatusText[i].rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(70f, -54f), new Vector2(-80f, 22f));
        }

        // 2. Haut-Centre : Bannière Événement du Semestre + Pop-up Carte Événement
        semesterEventBox = CreatePanel("SemesterEventBanner", inGameHudRoot.transform, new Color(0.86f, 0.58f, 0.08f, 0.96f));
        SetAnchoredRect(semesterEventBox.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(780f, 52f));
        semesterEventText = CreateText("Text", semesterEventBox.transform, "", 17, FontStyle.Bold, new Color(0.08f, 0.07f, 0.05f), TextAnchor.MiddleCenter);
        StretchFull(semesterEventText.rectTransform, 14f, 6f);
        semesterEventBox.SetActive(false);

        cardToastBox = CreatePanel("CardEventToast", inGameHudRoot.transform, new Color(0.46f, 0.22f, 0.78f, 0.96f));
        SetAnchoredRect(cardToastBox.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(780f, 48f));
        cardToastText = CreateText("Text", cardToastBox.transform, "", 16, FontStyle.Bold, ColTextMain, TextAnchor.MiddleCenter);
        StretchFull(cardToastText.rectTransform, 14f, 6f);
        cardToastBox.SetActive(false);

        // 3. Haut-Droite : Barre d'outils compacte
        GameObject toolbar = CreatePanel("TopRight_Toolbar", inGameHudRoot.transform, ColPanelDark);
        SetAnchoredRect(toolbar.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -18f), new Vector2(440f, 118f));

        Button bAccel = CreateButton("BtnAccelAI", toolbar.transform, "IA Rapide", ColCardIdle, 14, ToggleAccelerateAI);
        SetAnchoredRect(bAccel.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -10f), new Vector2(134f, 36f));
        btnAccelBg = bAccel.GetComponent<Image>();
        btnAccelLabel = bAccel.GetComponentInChildren<Text>();

        Button bTurbo = CreateButton("BtnTurbo", toolbar.transform, "Turbo [V]", ColCardIdle, 14, ToggleTurboMode);
        SetAnchoredRect(bTurbo.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(152f, -10f), new Vector2(134f, 36f));
        btnTurboBg = bTurbo.GetComponent<Image>();
        btnTurboLabel = bTurbo.GetComponentInChildren<Text>();

        Button bPause = CreateButton("BtnPause", toolbar.transform, "Pause [Échap]", ColAccentRed, 14, () => SetPaused(!isPaused));
        SetAnchoredRect(bPause.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(294f, -10f), new Vector2(136f, 36f));

        Button bCam = CreateButton("BtnCamera", toolbar.transform, "Caméra [R]", ColCardIdle, 14, RecenterCamera);
        SetAnchoredRect(bCam.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -52f), new Vector2(134f, 34f));

        Button bMute = CreateButton("BtnMute", toolbar.transform, "Son [M] : ON", ColCardIdle, 14, ToggleMute);
        SetAnchoredRect(bMute.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(152f, -52f), new Vector2(134f, 34f));
        btnMuteLabel = bMute.GetComponentInChildren<Text>();

        Button bHelp = CreateButton("BtnHelp", toolbar.transform, "Règles [H]", ColCardIdle, 14, () => SetRulesModalOpen(true));
        SetAnchoredRect(bHelp.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(294f, -52f), new Vector2(136f, 34f));

        Text hintTxt = CreateText("ControlsHint", toolbar.transform, "Clic case : inspecter · Clic droit / A-D / Flèches : caméra · Molette : zoom", 12, FontStyle.Normal, ColTextMuted, TextAnchor.MiddleCenter);
        SetAnchoredRect(hintTxt.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 5f), new Vector2(-12f, 22f));

        // 4. Bas-Centre : Lancer de dés + Décision Humaine + Bandeau Info + Bulle IA
        rollPromptPanel = CreatePanel("RollPromptPanel", inGameHudRoot.transform, new Color(0.11f, 0.28f, 0.56f, 0.95f));
        SetAnchoredRect(rollPromptPanel.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 134f), new Vector2(720f, 64f));
        rollPromptText = CreateText("Prompt", rollPromptPanel.transform, "C'est votre tour !", 18, FontStyle.Bold, ColTextMain, TextAnchor.MiddleLeft);
        SetAnchoredRect(rollPromptText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(-260f, 0f));
        rollActionButton = CreateButton("BtnRollDice", rollPromptPanel.transform, "Lancer les dés [Espace]", ColAccentGreen, 16, () =>
        {
            if (diceRoller != null && diceRoller.IsRolling)
            {
                ClearUISelection();
                diceRoller.SkipRollAnimation();
            }
            else
            {
                TriggerVirtualKey(Key.Space);
            }
        });
        SetAnchoredRect(rollActionButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(226f, 44f));
        rollActionBtnText = rollActionButton.GetComponentInChildren<Text>();
        rollPromptPanel.SetActive(false);

        decisionPanel = CreatePanel("DecisionPanel", inGameHudRoot.transform, new Color(0.09f, 0.14f, 0.24f, 0.96f));
        SetAnchoredRect(decisionPanel.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 134f), new Vector2(820f, 114f));

        // Barre de progression du chrono 10 s
        GameObject timerBg = CreatePanel("TimerBg", decisionPanel.transform, new Color(0.20f, 0.24f, 0.34f, 1f));
        SetAnchoredRect(timerBg.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 7f));
        GameObject timerFillObj = CreatePanel("TimerFill", timerBg.transform, ColAccentBlue);
        StretchFull(timerFillObj.GetComponent<RectTransform>());
        decisionTimerFill = timerFillObj.GetComponent<Image>();

        decisionPromptText = CreateText("DecisionPrompt", decisionPanel.transform, "", 18, FontStyle.Bold, ColTextMain, TextAnchor.MiddleCenter);
        SetAnchoredRect(decisionPromptText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(-24f, 40f));

        // Groupe Boutons Oui / Non
        yesNoGroup = CreateUIObject("YesNoGroup", decisionPanel.transform);
        SetAnchoredRect(yesNoGroup.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(440f, 44f));
        Button bYes = CreateButton("BtnYes", yesNoGroup.transform, "Oui [O]", ColAccentGreen, 17, () => TriggerVirtualKey(Key.O));
        SetAnchoredRect(bYes.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(205f, 42f));
        Button bNo = CreateButton("BtnNo", yesNoGroup.transform, "Non [N]", ColAccentRed, 17, () => TriggerVirtualKey(Key.N));
        SetAnchoredRect(bNo.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(205f, 42f));

        // Groupe Boutons Colle (P / B / T)
        detentionGroup = CreateUIObject("DetentionGroup", decisionPanel.transform);
        SetAnchoredRect(detentionGroup.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(680f, 44f));
        btnDetentionPay = CreateButton("BtnPayFine", detentionGroup.transform, "Payer 50 € [P]", ColAccentAmber, 16, () => TriggerVirtualKey(Key.P));
        SetAnchoredRect(btnDetentionPay.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(210f, 42f));
        btnDetentionPayText = btnDetentionPay.GetComponentInChildren<Text>();

        btnDetentionTicket = CreateButton("BtnUseTicket", detentionGroup.transform, "Billet d'absence [B]", ColAccentPurple, 16, () => TriggerVirtualKey(Key.B));
        SetAnchoredRect(btnDetentionTicket.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220f, 42f));

        Button btnDetentionWait = CreateButton("BtnWaitDetention", detentionGroup.transform, "Attendre 1 tour [T]", ColCardIdle, 16, () => TriggerVirtualKey(Key.T));
        SetAnchoredRect(btnDetentionWait.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(210f, 42f));
        decisionPanel.SetActive(false);

        // Zone 1 : Info Système
        infoBannerBox = CreatePanel("InfoBannerBox", inGameHudRoot.transform, new Color(0.14f, 0.22f, 0.34f, 0.92f));
        SetAnchoredRect(infoBannerBox.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 76f), new Vector2(780f, 44f));
        infoBannerText = CreateText("Text", infoBannerBox.transform, "", 16, FontStyle.Bold, ColTextMain, TextAnchor.MiddleCenter);
        StretchFull(infoBannerText.rectTransform, 14f, 4f);
        infoBannerBox.SetActive(false);

        // Zone 2 : Bulle IA (AIBarkController)
        aiBubbleBox = CreatePanel("AISpeechBubble", inGameHudRoot.transform, new Color(0.10f, 0.62f, 0.42f, 0.95f));
        SetAnchoredRect(aiBubbleBox.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(780f, 48f));
        aiBubbleText = CreateText("Text", aiBubbleBox.transform, "", 17, FontStyle.Italic, Color.white, TextAnchor.MiddleCenter);
        StretchFull(aiBubbleText.rectTransform, 16f, 4f);
        aiBubbleBox.SetActive(false);
    }

    private void BuildTileInspectorPanel(Transform parent)
    {
        tileInspectorRoot = CreatePanel("TileInspectorCard", parent, ColPanelDark);
        SetAnchoredRect(tileInspectorRoot.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-18f, -15f), new Vector2(360f, 360f));

        GameObject header = CreatePanel("HeaderStrip", tileInspectorRoot.transform, ColAccentBlue);
        SetAnchoredRect(header.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 54f));
        inspectorHeaderBg = header.GetComponent<Image>();

        inspectorTitleText = CreateText("Title", header.transform, "Case", 18, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft);
        SetAnchoredRect(inspectorTitleText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(-56f, 0f));

        Button closeBtn = CreateButton("BtnClose", header.transform, "X", new Color(0f, 0f, 0f, 0.35f), 15, () =>
        {
            ClearUISelection();
            if (tileInspector != null) tileInspector.ClearSelection();
        });
        SetAnchoredRect(closeBtn.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(34f, 34f));

        inspectorSubtitleText = CreateText("Subtitle", tileInspectorRoot.transform, "", 14, FontStyle.Bold, ColAccentAmber, TextAnchor.MiddleLeft);
        SetAnchoredRect(inspectorSubtitleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -62f), new Vector2(-28f, 26f));

        inspectorBodyText = CreateText("Body", tileInspectorRoot.transform, "", 15, FontStyle.Normal, ColTextMain, TextAnchor.UpperLeft);
        SetAnchoredRect(inspectorBodyText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -42f), new Vector2(-28f, -104f));

        tileInspectorRoot.SetActive(false);
    }

    private void BuildMainMenuOverlay(Transform parent)
    {
        mainMenuRoot = CreatePanel("MainMenuOverlay", parent, new Color(0.05f, 0.07f, 0.12f, 0.58f));
        StretchFull(mainMenuRoot.GetComponent<RectTransform>());

        GameObject card = CreatePanel("MenuCard", mainMenuRoot.transform, new Color(0.08f, 0.10f, 0.16f, 0.94f));
        SetAnchoredRect(card.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1060f, 740f));

        Text title = CreateText("Title", card.transform, "CAMPUS TYCOON", 42, FontStyle.Bold, ColTextMain, TextAnchor.MiddleCenter);
        SetAnchoredRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(-40f, 56f));

        Text sub = CreateText("Subtitle", card.transform, "Conquérez le campus · 3 niveaux · 3 personnages aux pouvoirs uniques · 2 rivaux IA", 17, FontStyle.Italic, ColTextMuted, TextAnchor.MiddleCenter);
        SetAnchoredRect(sub.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -74f), new Vector2(-40f, 28f));

        // Colonne Gauche : Choix du Niveau
        Text lblLvl = CreateText("LblLevel", card.transform, "1. CHOISISSEZ LE NIVEAU", 18, FontStyle.Bold, ColAccentAmber, TextAnchor.MiddleLeft);
        SetAnchoredRect(lblLvl.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(36f, -122f), new Vector2(480f, 30f));

        for (int i = 0; i < 3; i++)
        {
            int captured = i;
            Button btn = CreateButton($"LevelBtn_{i}", card.transform, $"Niveau {i + 1}", ColCardIdle, 15, () =>
            {
                ClearUISelection();
                PlayClickSound();
                menuSelectedLevel = captured;
                if (launcher != null) launcher.PreviewSelectionOnBoard(menuSelectedLevel, menuSelectedChar);
                RefreshMainMenuCards();
            });
            SetAnchoredRect(btn.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(36f, -162f - i * 124f), new Vector2(480f, 110f));
            Text t = btn.GetComponentInChildren<Text>();
            t.alignment = TextAnchor.MiddleLeft;
            StretchFull(t.rectTransform, 16f, 8f);
            menuLevelButtons[i] = btn;
            menuLevelBgs[i] = btn.GetComponent<Image>();
            menuLevelTexts[i] = t;
        }

        // Colonne Droite : Choix du Personnage
        Text lblChar = CreateText("LblChar", card.transform, "2. CHOISISSEZ VOTRE PERSONNAGE", 18, FontStyle.Bold, ColAccentAmber, TextAnchor.MiddleLeft);
        SetAnchoredRect(lblChar.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-36f, -122f), new Vector2(480f, 30f));

        for (int i = 0; i < 3; i++)
        {
            int captured = i;
            Button btn = CreateButton($"CharBtn_{i}", card.transform, $"Personnage {i + 1}", ColCardIdle, 15, () =>
            {
                ClearUISelection();
                PlayClickSound();
                menuSelectedChar = captured;
                if (launcher != null) launcher.PreviewSelectionOnBoard(menuSelectedLevel, menuSelectedChar);
                RefreshMainMenuCards();
            });
            SetAnchoredRect(btn.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-36f, -162f - i * 124f), new Vector2(480f, 110f));
            Text t = btn.GetComponentInChildren<Text>();
            t.alignment = TextAnchor.MiddleLeft;
            StretchFull(t.rectTransform, 16f, 8f);
            menuCharButtons[i] = btn;
            menuCharBgs[i] = btn.GetComponent<Image>();
            menuCharTexts[i] = t;
        }

        menuSelectionSummaryText = CreateText("Summary", card.transform, "", 16, FontStyle.Bold, new Color(0.80f, 0.92f, 1f), TextAnchor.MiddleCenter);
        SetAnchoredRect(menuSelectionSummaryText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 108f), new Vector2(-40f, 32f));

        // Barre d'actions du Menu Principal
        Button btnPlay = CreateButton("BtnStartGame", card.transform, "LANCER LA PARTIE", ColAccentGreen, 22, () =>
        {
            ClearUISelection();
            PlayClickSound();
            if (launcher != null) launcher.StartGameFromMenu(menuSelectedLevel, menuSelectedChar);
        });
        SetAnchoredRect(btnPlay.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 32f), new Vector2(360f, 60f));

        Button btnMenuRules = CreateButton("BtnMenuRules", card.transform, "Règles & Commandes", ColCardIdle, 16, () => SetRulesModalOpen(true));
        SetAnchoredRect(btnMenuRules.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(36f, 38f), new Vector2(240f, 48f));

        Button btnMenuQuit = CreateButton("BtnMenuQuit", card.transform, "Quitter", ColAccentRed, 16, () =>
        {
            if (launcher != null) launcher.QuitApplication();
            else Application.Quit();
        });
        SetAnchoredRect(btnMenuQuit.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-36f, 38f), new Vector2(180f, 48f));

        mainMenuRoot.SetActive(false);
    }

    private void BuildPauseModal(Transform parent)
    {
        pauseModalRoot = CreatePanel("PauseModal", parent, new Color(0.03f, 0.04f, 0.08f, 0.72f));
        StretchFull(pauseModalRoot.GetComponent<RectTransform>());

        GameObject box = CreatePanel("PauseBox", pauseModalRoot.transform, ColPanelDark);
        SetAnchoredRect(box.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(440f, 505f));

        Text title = CreateText("Title", box.transform, "PAUSE", 30, FontStyle.Bold, ColTextMain, TextAnchor.MiddleCenter);
        SetAnchoredRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(0f, 44f));

        float y = -82f;
        AddPauseButton(box.transform, "Reprendre la partie", ColAccentGreen, ref y, () => SetPaused(false));
        AddPauseButton(box.transform, "Règles & Commandes", ColCardIdle, ref y, () => SetRulesModalOpen(true));
        Button bMusic = AddPauseButton(box.transform, "Musique calme : ON", ColCardIdle, ref y, ToggleMusic);
        pauseMusicBtnText = bMusic.GetComponentInChildren<Text>();
        Button bMute = AddPauseButton(box.transform, "Couper tout le son", ColCardIdle, ref y, ToggleMute);
        pauseMuteBtnText = bMute.GetComponentInChildren<Text>();
        AddPauseButton(box.transform, "Recommencer ce niveau", ColAccentBlue, ref y, () =>
        {
            if (launcher != null) launcher.RestartCurrentGame();
        });
        AddPauseButton(box.transform, "Retour au Menu Principal", ColAccentAmber, ref y, () =>
        {
            if (launcher != null) launcher.ReturnToMainMenu();
        });
        AddPauseButton(box.transform, "Quitter le jeu", ColAccentRed, ref y, () =>
        {
            if (launcher != null) launcher.QuitApplication();
            else Application.Quit();
        });

        pauseModalRoot.SetActive(false);
    }

    private Button AddPauseButton(Transform parent, string label, Color col, ref float y, UnityEngine.Events.UnityAction onClick)
    {
        Button b = CreateButton("PauseBtn_" + label, parent, label, col, 17, onClick);
        SetAnchoredRect(b.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(360f, 46f));
        y -= 56f;
        return b;
    }

    private void BuildGameOverModal(Transform parent)
    {
        gameOverModalRoot = CreatePanel("GameOverModal", parent, new Color(0.03f, 0.04f, 0.08f, 0.78f));
        StretchFull(gameOverModalRoot.GetComponent<RectTransform>());

        GameObject box = CreatePanel("GameOverBox", gameOverModalRoot.transform, new Color(0.08f, 0.10f, 0.16f, 0.96f));
        SetAnchoredRect(box.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(780f, 520f));

        gameOverTitleText = CreateText("Title", box.transform, "FIN DE PARTIE", 28, FontStyle.Bold, ColAccentGreen, TextAnchor.MiddleCenter);
        SetAnchoredRect(gameOverTitleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(-36f, 48f));

        gameOverSubtitleText = CreateText("Subtitle", box.transform, "", 18, FontStyle.Bold, ColTextMain, TextAnchor.MiddleCenter);
        SetAnchoredRect(gameOverSubtitleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(-36f, 34f));

        GameObject boardBg = CreatePanel("LeaderboardBg", box.transform, ColCardIdle);
        SetAnchoredRect(boardBg.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -124f), new Vector2(700f, 250f));
        gameOverLeaderboardText = CreateText("Leaderboard", boardBg.transform, "", 17, FontStyle.Normal, ColTextMain, TextAnchor.MiddleLeft);
        StretchFull(gameOverLeaderboardText.rectTransform, 22f, 14f);

        // Boutons Fin de Partie (4 boutons régulièrement espacés dans 780 px)
        Button bReplay = CreateButton("BtnReplay", box.transform, "Rejouer", ColAccentGreen, 17, () =>
        {
            if (launcher != null) launcher.RestartCurrentGame();
        });
        SetAnchoredRect(bReplay.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 36f), new Vector2(160f, 50f));

        btnNextLevel = CreateButton("BtnNextLevel", box.transform, "Niveau suivant", ColAccentBlue, 17, () =>
        {
            if (launcher != null) launcher.LaunchNextLevel();
        });
        SetAnchoredRect(btnNextLevel.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(214f, 36f), new Vector2(166f, 50f));

        Button bMenu = CreateButton("BtnReturnMenu", box.transform, "Menu Principal", ColAccentAmber, 17, () =>
        {
            if (launcher != null) launcher.ReturnToMainMenu();
        });
        SetAnchoredRect(bMenu.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(394f, 36f), new Vector2(172f, 50f));

        Button bQuit = CreateButton("BtnQuitEnd", box.transform, "Quitter", ColAccentRed, 17, () =>
        {
            if (launcher != null) launcher.QuitApplication();
            else Application.Quit();
        });
        SetAnchoredRect(bQuit.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(580f, 36f), new Vector2(160f, 50f));

        gameOverModalRoot.SetActive(false);
    }

    private void BuildRulesModal(Transform parent)
    {
        rulesModalRoot = CreatePanel("RulesModal", parent, new Color(0.03f, 0.04f, 0.08f, 0.78f));
        StretchFull(rulesModalRoot.GetComponent<RectTransform>());

        GameObject box = CreatePanel("RulesBox", rulesModalRoot.transform, new Color(0.08f, 0.11f, 0.17f, 0.97f));
        SetAnchoredRect(box.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 560f));

        Text title = CreateText("Title", box.transform, "RÈGLES RAPIDES & COMMANDES — CAMPUS TYCOON", 22, FontStyle.Bold, ColAccentAmber, TextAnchor.MiddleCenter);
        SetAnchoredRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(-40f, 40f));

        string rulesText =
            "OBJECTIF\n" +
            "Terminer le semestre (12 ou 15 tours) avec la plus grande fortune totale (argent + valeur des terrains + coût des étages), ou éliminer les 2 rivaux IA par faillite.\n\n" +
            "RÈGLES CLÉS\n" +
            "- Passer par La Rentrée rapporte +150 €. Un double permet de rejouer une seule fois.\n" +
            "- Posséder les 2 terrains d'un groupe double le loyer (2R) et débloque les améliorations (Niv. 1 = 5R, Niv. 2 = 12R, Niv. 3 = 22R · 1 niveau max par terrain et par tour).\n" +
            "- Chaque personnage dispose d'un pouvoir unique utilisable 1 fois par partie.\n" +
            "- Niveau 3 : la Roulette du BDE permet de miser 50 € sur 1 dé (1-2 : 0 € · 3-4 : 100 € · 5-6 : 200 €).\n" +
            "- En cas de dette, vos biens sont revendus à 50 %. Si cela ne suffit pas, c'est la faillite !\n\n" +
            "COMMANDES (CLAVIER ET SOURIS)\n" +
            "- Lancer les dés : [Espace] ou bouton dédié · Passer l'animation des dés : [Clic gauche]\n" +
            "- Décisions (Achat, Étage, Pouvoir, Roulette, Billet) : [O] Oui / [N] Non ou boutons à l'écran (chrono 10 s)\n" +
            "- Sortir de Colle : [P] Payer 50 € · [B] Billet d'absence · [T] Attendre 1 tour\n" +
            "- Inspecter une case : [Clic gauche] sur n'importe quelle tuile 3D du plateau\n" +
            "- Caméra : [Clic droit + Souris] orbiter · [A / Q / D] ou [Flèches] tourner · [Molette] zoom · [R] recentrer\n" +
            "- Raccourcis : [V] Mode Turbo · [M] Son ON/OFF · [H] Aide · [Échap] Pause";

        Text body = CreateText("Body", box.transform, rulesText, 16, FontStyle.Normal, ColTextMain, TextAnchor.UpperLeft);
        SetAnchoredRect(body.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -6f), new Vector2(-56f, -150f));

        Button closeBtn = CreateButton("BtnCloseRules", box.transform, "Fermer", ColAccentBlue, 17, () => SetRulesModalOpen(false));
        SetAnchoredRect(closeBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(200f, 46f));

        rulesModalRoot.SetActive(false);
    }

    // ==================== UTILITAIRES DE CONSTRUCTION UGUI ====================

    private static GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static GameObject CreatePanel(string name, Transform parent, Color color)
    {
        GameObject go = CreateUIObject(name, parent);
        Image img = go.AddComponent<Image>();
        img.color = color;
        return go;
    }

    private Text CreateText(string name, Transform parent, string text, int fontSize, FontStyle style, Color color, TextAnchor alignment)
    {
        GameObject go = CreateUIObject(name, parent);
        Text t = go.AddComponent<Text>();
        t.font = uiFont;
        t.text = text;
        t.fontSize = fontSize;
        t.fontStyle = style;
        t.color = color;
        t.alignment = alignment;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    private Button CreateButton(string name, Transform parent, string label, Color bgColor, int fontSize, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = CreatePanel(name, parent, bgColor);
        Button btn = go.AddComponent<Button>();
        btn.navigation = new Navigation { mode = Navigation.Mode.None }; // Empêche [Espace] de recliquer le dernier bouton UI !

        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        cb.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        cb.disabledColor = new Color(0.45f, 0.45f, 0.48f, 0.55f);
        btn.colors = cb;

        if (onClick != null) btn.onClick.AddListener(onClick);

        Text txt = CreateText("Label", go.transform, label, fontSize, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
        StretchFull(txt.rectTransform, 8f, 4f);
        return btn;
    }

    private static void StretchFull(RectTransform rt, float padX = 0f, float padY = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padX, padY);
        rt.offsetMax = new Vector2(-padX, -padY);
    }

    private static void SetAnchoredRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }
}
