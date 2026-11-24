using System.Collections.Generic;
using CampusTycoon;
using UnityEngine;

/// <summary>
/// Gestionnaire audio 100 % autonome (synthèse procédurale d'effets sonores en C#).
/// Écoute CampusEvents sans aucune dépendance à des fichiers audio externes.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class CampusAudioManager : MonoBehaviour
{
    public static CampusAudioManager Instance { get; private set; }

    [Header("Réglages Audio")]
    [Range(0f, 1f)] public float masterVolume = 0.55f;
    public bool isMuted = false;

    private const string MutePrefKey = "CampusTycoon_AudioMuted";
    private const int SampleRate = 44100;

    private AudioSource sfxSource;
    private readonly Dictionary<int, int> knownTileLevels = new Dictionary<int, int>();

    private AudioClip clipDice;
    private AudioClip clipHop;
    private AudioClip clipBuy;
    private AudioClip clipUpgrade;
    private AudioClip clipSell;
    private AudioClip clipRent;
    private AudioClip clipCard;
    private AudioClip clipGlobalEvent;
    private AudioClip clipPower;
    private AudioClip clipDetention;
    private AudioClip clipVictory;
    private AudioClip clipDefeat;
    private AudioClip clipUIClick;

    void Awake()
    {
        Instance = this;
        sfxSource = GetComponent<AudioSource>();
        if (sfxSource == null) sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.spatialBlend = 0f;

        isMuted = PlayerPrefs.GetInt(MutePrefKey, 0) == 1;
        BuildProceduralClips();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        CampusEvents.OnDiceRolled += HandleDiceRolled;
        CampusEvents.OnPropertyBought += HandlePropertyBought;
        CampusEvents.OnPropertyUpgraded += HandlePropertyUpgraded;
        CampusEvents.OnRentPaid += HandleRentPaid;
        CampusEvents.OnCardDrawn += HandleCardDrawn;
        CampusEvents.OnGlobalEventStarted += HandleGlobalEventStarted;
        CampusEvents.OnPowerUsed += HandlePowerUsed;
        CampusEvents.OnDetentionChanged += HandleDetentionChanged;
        CampusEvents.OnPlayerBankrupt += HandlePlayerBankrupt;
        CampusEvents.OnGameOver += HandleGameOver;
    }

    void OnDisable()
    {
        CampusEvents.OnDiceRolled -= HandleDiceRolled;
        CampusEvents.OnPropertyBought -= HandlePropertyBought;
        CampusEvents.OnPropertyUpgraded -= HandlePropertyUpgraded;
        CampusEvents.OnRentPaid -= HandleRentPaid;
        CampusEvents.OnCardDrawn -= HandleCardDrawn;
        CampusEvents.OnGlobalEventStarted -= HandleGlobalEventStarted;
        CampusEvents.OnPowerUsed -= HandlePowerUsed;
        CampusEvents.OnDetentionChanged -= HandleDetentionChanged;
        CampusEvents.OnPlayerBankrupt -= HandlePlayerBankrupt;
        CampusEvents.OnGameOver -= HandleGameOver;
    }

    public void ToggleMute()
    {
        isMuted = !isMuted;
        PlayerPrefs.SetInt(MutePrefKey, isMuted ? 1 : 0);
        PlayerPrefs.Save();
        if (!isMuted) PlayUIClick();
    }

    public static void PlayPawnHopStatic()
    {
        if (Instance != null) Instance.PlayPawnHop();
    }

    public void PlayUIClick() => PlayClip(clipUIClick, 0.45f, 1.0f);
    public void PlayPawnHop() => PlayClip(clipHop, 0.35f, Random.Range(0.92f, 1.10f));
    public void PlayDiceRoll() => PlayClip(clipDice, 0.55f, Random.Range(0.95f, 1.06f));

    private void PlayClip(AudioClip clip, float scale = 1f, float pitch = 1f)
    {
        if (isMuted || clip == null || sfxSource == null) return;
        sfxSource.pitch = pitch;
        sfxSource.PlayOneShot(clip, Mathf.Clamp01(masterVolume * scale));
    }

    // ==================== RÉACTIONS AUX ÉVÉNEMENTS ====================

    private void HandleDiceRolled(int playerId, int d1, int d2, bool isDouble)
    {
        PlayDiceRoll();
    }

    private void HandlePropertyBought(int playerId, int tileIndex, int price)
    {
        knownTileLevels[tileIndex] = 0;
        PlayClip(clipBuy, 0.75f, 1f);
    }

    private void HandlePropertyUpgraded(int playerId, int tileIndex, int newLevel)
    {
        int prev = knownTileLevels.TryGetValue(tileIndex, out int oldLvl) ? oldLvl : 0;
        knownTileLevels[tileIndex] = newLevel;
        if (newLevel >= prev && newLevel > 0)
        {
            PlayClip(clipUpgrade, 0.8f, 1f + 0.08f * (newLevel - 1));
        }
        else
        {
            PlayClip(clipSell, 0.7f, 1f);
        }
    }

    private void HandleRentPaid(int tenantId, int ownerId, int tileIndex, int amount)
    {
        PlayClip(clipRent, 0.75f, ownerId == 0 ? 1.12f : 0.90f);
    }

    private void HandleCardDrawn(int playerId, int cardId, string title, string desc)
    {
        PlayClip(clipCard, 0.7f, 1f);
    }

    private void HandleGlobalEventStarted(string title, string desc)
    {
        PlayClip(clipGlobalEvent, 0.85f, 1f);
    }

    private void HandlePowerUsed(int playerId, string charName, string powerName, string desc)
    {
        PlayClip(clipPower, 0.85f, 1f);
    }

    private void HandleDetentionChanged(int playerId, bool inDetention)
    {
        if (inDetention) PlayClip(clipDetention, 0.8f, 1f);
    }

    private void HandlePlayerBankrupt(int playerId, int creditorId)
    {
        PlayClip(clipDefeat, 0.85f, 0.9f);
    }

    private void HandleGameOver(int winnerId, int finalScore, bool isHumanVictory)
    {
        PlayClip(isHumanVictory ? clipVictory : clipDefeat, 0.95f, 1f);
    }

    // ==================== SYNTHÈSE PROCÉDURALE PROPRE (ADSR DOUX) ====================

    private void BuildProceduralClips()
    {
        clipUIClick = CreateToneSequence("SFX_UIClick", new[] { (760f, 0.045f, 0.35f) });
        clipHop = CreateToneSequence("SFX_Hop", new[] { (310f, 0.055f, 0.40f), (440f, 0.045f, 0.25f) });
        clipDice = CreateDiceRattleClip("SFX_Dice");
        clipBuy = CreateToneSequence("SFX_Buy", new[] { (523.25f, 0.09f, 0.45f), (659.25f, 0.14f, 0.50f) });
        clipUpgrade = CreateToneSequence("SFX_Upgrade", new[] { (523.25f, 0.08f, 0.42f), (659.25f, 0.08f, 0.45f), (783.99f, 0.18f, 0.55f) });
        clipSell = CreateToneSequence("SFX_Sell", new[] { (493.88f, 0.10f, 0.42f), (369.99f, 0.16f, 0.45f) });
        clipRent = CreateToneSequence("SFX_Rent", new[] { (880f, 0.07f, 0.38f), (1174.66f, 0.13f, 0.42f) });
        clipCard = CreateToneSequence("SFX_Card", new[] { (440f, 0.07f, 0.35f), (587.33f, 0.07f, 0.40f), (880f, 0.12f, 0.35f) });
        clipGlobalEvent = CreateToneSequence("SFX_GlobalEvent", new[] { (587.33f, 0.12f, 0.42f), (783.99f, 0.12f, 0.45f), (1046.5f, 0.28f, 0.48f) });
        clipPower = CreateToneSequence("SFX_Power", new[] { (440f, 0.06f, 0.40f), (554.37f, 0.06f, 0.44f), (659.25f, 0.06f, 0.46f), (880f, 0.22f, 0.50f) });
        clipDetention = CreateToneSequence("SFX_Detention", new[] { (246.94f, 0.16f, 0.45f), (185.00f, 0.28f, 0.50f) });
        clipVictory = CreateToneSequence("SFX_Victory", new[] { (523.25f, 0.10f, 0.45f), (659.25f, 0.10f, 0.48f), (783.99f, 0.10f, 0.50f), (1046.5f, 0.35f, 0.58f) });
        clipDefeat = CreateToneSequence("SFX_Defeat", new[] { (392.00f, 0.16f, 0.45f), (349.23f, 0.16f, 0.45f), (311.13f, 0.18f, 0.45f), (261.63f, 0.35f, 0.48f) });
    }

    private static AudioClip CreateToneSequence(string clipName, (float freq, float duration, float amp)[] notes)
    {
        int totalSamples = 0;
        for (int i = 0; i < notes.Length; i++)
        {
            totalSamples += Mathf.Max(1, Mathf.RoundToInt(notes[i].duration * SampleRate));
        }

        float[] data = new float[totalSamples];
        int cursor = 0;

        for (int n = 0; n < notes.Length; n++)
        {
            float freq = notes[n].freq;
            int len = Mathf.Max(1, Mathf.RoundToInt(notes[n].duration * SampleRate));
            float amp = notes[n].amp;

            for (int i = 0; i < len && (cursor + i) < totalSamples; i++)
            {
                float t = (float)i / SampleRate;
                float u = (float)i / len;
                // Enveloppe douce sans clic (attaque 8 %, relâchement exponentiel)
                float env = u < 0.08f ? (u / 0.08f) : Mathf.Pow(1f - u, 1.5f);
                // Fondamentale + légère harmonique douce pour un son chaleureux
                float wave = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.78f
                           + Mathf.Sin(4f * Mathf.PI * freq * t) * 0.18f
                           + Mathf.Sin(6f * Mathf.PI * freq * t) * 0.04f;
                data[cursor + i] = wave * env * amp;
            }
            cursor += len;
        }

        AudioClip clip = AudioClip.Create(clipName, totalSamples, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateDiceRattleClip(string clipName)
    {
        int totalSamples = Mathf.RoundToInt(0.26f * SampleRate);
        float[] data = new float[totalSamples];
        System.Random rng = new System.Random(42);

        // 4 petits clics boisés simulant des dés qui rebondissent sur le plateau
        float[] hitTimes = { 0.01f, 0.065f, 0.13f, 0.185f };
        float[] hitPitch = { 620f, 740f, 540f, 680f };

        for (int h = 0; h < hitTimes.Length; h++)
        {
            int start = Mathf.RoundToInt(hitTimes[h] * SampleRate);
            int len = Mathf.RoundToInt(0.045f * SampleRate);
            for (int i = 0; i < len && (start + i) < totalSamples; i++)
            {
                float t = (float)i / SampleRate;
                float u = (float)i / len;
                float env = Mathf.Exp(-u * 6.5f);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.25f;
                float wood = Mathf.Sin(2f * Mathf.PI * hitPitch[h] * t) * 0.65f;
                data[start + i] += (wood + noise) * env * 0.42f;
            }
        }

        AudioClip clip = AudioClip.Create(clipName, totalSamples, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
