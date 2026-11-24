using System.Collections.Generic;
using CampusTycoon;
using UnityEngine;

/// <summary>
/// Gestionnaire audio 100 % autonome (synthèse procédurale en C#, zéro fichier audio externe) :
/// - Musique d'ambiance calme (boucle piano électrique / lo-fi campus 16 s sans couture)
/// - 13 effets sonores doux abonnés à CampusEvents
/// - Contrôle indépendant Son global ([M]) et Musique (bouton Pause / HUD) persistés en PlayerPrefs
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class CampusAudioManager : MonoBehaviour
{
    public static CampusAudioManager Instance { get; private set; }

    [Header("Réglages Audio")]
    [Range(0f, 1f)] public float masterVolume = 0.55f;
    [Range(0f, 0.6f)] public float musicVolume = 0.22f;
    public bool isMuted = false;
    public bool isMusicMuted = false;

    private const string MutePrefKey = "CampusTycoon_AudioMuted";
    private const string MusicMutePrefKey = "CampusTycoon_MusicMuted";
    private const int SampleRate = 44100;
    private const int MusicSampleRate = 22050; // Léger en RAM, très doux dans les aigus (style lo-fi chaud)

    private AudioSource sfxSource;
    private AudioSource musicSource;
    private float musicFadeProgress = 0f;
    private readonly Dictionary<int, int> knownTileLevels = new Dictionary<int, int>();

    private AudioClip clipAmbientMusic;
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
        sfxSource.loop = false;
        sfxSource.spatialBlend = 0f;

        isMuted = PlayerPrefs.GetInt(MutePrefKey, 0) == 1;
        isMusicMuted = PlayerPrefs.GetInt(MusicMutePrefKey, 0) == 1;

        BuildProceduralClips();
        SetupCalmAmbientMusic();
    }

    void Update()
    {
        if (musicSource == null) return;

        // Fondu d'entrée doux sur 2 secondes + atténuation légère quand le jeu est en Pause
        if (musicFadeProgress < 1f)
        {
            musicFadeProgress = Mathf.Min(1f, musicFadeProgress + Time.unscaledDeltaTime * 0.5f);
        }

        bool shouldPlayMusic = !isMuted && !isMusicMuted;
        float pauseDuck = Time.timeScale <= 0.01f ? 0.45f : 1f;
        float targetVol = shouldPlayMusic ? (musicVolume * musicFadeProgress * pauseDuck) : 0f;
        musicSource.volume = Mathf.Lerp(musicSource.volume, targetVol, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
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

    public void ToggleMusic()
    {
        isMusicMuted = !isMusicMuted;
        PlayerPrefs.SetInt(MusicMutePrefKey, isMusicMuted ? 1 : 0);
        PlayerPrefs.Save();
        PlayUIClick();
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

    // ==================== MUSIQUE D'AMBIANCE CALME (PIANO FEUTRÉ LO-FI 16 s) ====================

    private void SetupCalmAmbientMusic()
    {
        GameObject musicChild = new GameObject("CampusAmbientMusic");
        musicChild.transform.SetParent(transform, false);
        musicSource = musicChild.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.loop = true;
        musicSource.spatialBlend = 0f;
        musicSource.volume = 0f;

        clipAmbientMusic = CreateCalmCampusMusicLoop();
        musicSource.clip = clipAmbientMusic;
        musicSource.Play();
    }

    /// <summary>
    /// Génère une boucle musicale calme de 16 secondes (style Rhodes / piano feutré universitaire) :
    /// Progression douce Fmaj7 -> Em7 -> Dm9 -> G13sus4/Cmaj9, avec écho doux bouclé sans coupure.
    /// </summary>
    private static AudioClip CreateCalmCampusMusicLoop()
    {
        const float loopSeconds = 16.0f;
        int totalSamples = Mathf.RoundToInt(loopSeconds * MusicSampleRate);
        float[] buffer = new float[totalSamples];

        // 4 accords chaleureux de 4 secondes chacun (en Hz)
        float[][] chords =
        {
            new[] { 174.61f, 220.00f, 261.63f, 329.63f }, // Fmaj7 : F3, A3, C4, E4
            new[] { 164.81f, 196.00f, 246.94f, 293.66f }, // Em7   : E3, G3, B3, D4
            new[] { 146.83f, 174.61f, 220.00f, 261.63f }, // Dm7   : D3, F3, A3, C4
            new[] { 130.81f, 196.00f, 246.94f, 293.66f }  // Cmaj9 : C3, G3, B3, D4
        };
        float[] bassNotes = { 87.31f, 82.41f, 73.42f, 65.41f }; // F2, E2, D2, C2

        // 1. Nappes d'accords piano électrique doux (plaqués en arpège très léger + basse chaude)
        for (int c = 0; c < 4; c++)
        {
            float barStart = c * 4.0f;
            AddWarmNoteWrapped(buffer, totalSamples, bassNotes[c], barStart, 3.8f, 0.16f, isBass: true);

            for (int n = 0; n < chords[c].Length; n++)
            {
                float strumDelay = n * 0.055f;
                AddWarmNoteWrapped(buffer, totalSamples, chords[c][n], barStart + strumDelay, 3.6f, 0.095f, isBass: false);
            }

            // Douce reprise rythmique au 3e temps (2.0 s) pour donner une respiration apaisante
            AddWarmNoteWrapped(buffer, totalSamples, chords[c][1], barStart + 2.0f, 1.9f, 0.065f, isBass: false);
            AddWarmNoteWrapped(buffer, totalSamples, chords[c][3], barStart + 2.04f, 1.9f, 0.065f, isBass: false);
        }

        // 2. Petite mélodie apaisante de piano feutré dans le registre médium-aigu
        (float time, float freq, float dur, float amp)[] melody =
        {
            (0.50f, 440.00f, 1.4f, 0.075f), // A4
            (1.50f, 392.00f, 1.2f, 0.070f), // G4
            (2.75f, 329.63f, 1.4f, 0.070f), // E4

            (4.50f, 392.00f, 1.5f, 0.075f), // G4
            (6.00f, 329.63f, 1.1f, 0.065f), // E4
            (7.00f, 293.66f, 1.4f, 0.070f), // D4

            (8.50f, 349.23f, 1.4f, 0.075f), // F4
            (9.75f, 440.00f, 1.3f, 0.075f), // A4
            (11.00f, 392.00f, 1.3f, 0.068f), // G4

            (12.50f, 329.63f, 1.6f, 0.072f), // E4
            (14.25f, 293.66f, 1.4f, 0.065f), // D4
            (15.25f, 329.63f, 1.2f, 0.060f)  // E4 -> résout doucement vers Fmaj7
        };

        for (int i = 0; i < melody.Length; i++)
        {
            AddWarmNoteWrapped(buffer, totalSamples, melody[i].freq, melody[i].time, melody[i].dur, melody[i].amp, isBass: false);
        }

        // 3. Léger effet d'écho/réverbération circulaire (bouclage parfait sans clic)
        float[] wet = new float[totalSamples];
        int tap1 = Mathf.RoundToInt(0.36f * MusicSampleRate);
        int tap2 = Mathf.RoundToInt(0.62f * MusicSampleRate);
        for (int i = 0; i < totalSamples; i++)
        {
            int i1 = (i - tap1 + totalSamples) % totalSamples;
            int i2 = (i - tap2 + totalSamples) % totalSamples;
            wet[i] = buffer[i] + buffer[i1] * 0.24f + buffer[i2] * 0.13f;
        }

        // 4. Filtre passe-bas doux (1 pôle) + normalisation propre
        float lp = 0f;
        float peak = 0.001f;
        for (int i = 0; i < totalSamples; i++)
        {
            lp = lp + 0.35f * (wet[i] - lp);
            wet[i] = lp;
            float abs = Mathf.Abs(lp);
            if (abs > peak) peak = abs;
        }

        float gain = Mathf.Min(1f, 0.78f / peak);
        for (int i = 0; i < totalSamples; i++)
        {
            wet[i] *= gain;
        }

        AudioClip clip = AudioClip.Create("BGM_Campus_CalmLoop", totalSamples, 1, MusicSampleRate, false);
        clip.SetData(wet, 0);
        return clip;
    }

    private static void AddWarmNoteWrapped(float[] buffer, int totalSamples, float freq, float startSec, float durSec, float amp, bool isBass)
    {
        int startIdx = Mathf.RoundToInt(startSec * MusicSampleRate);
        int length = Mathf.RoundToInt(durSec * MusicSampleRate);
        float twoPi = 2f * Mathf.PI;

        for (int i = 0; i < length; i++)
        {
            float t = (float)i / MusicSampleRate;
            float u = (float)i / length;

            // Attaque très douce (35 ms) et déclin naturel type piano feutré
            float attack = Mathf.Clamp01(t / 0.035f);
            float decay = Mathf.Exp(-t * (isBass ? 0.65f : 1.05f));
            float tailFade = u > 0.85f ? Mathf.Clamp01((1f - u) / 0.15f) : 1f;
            float env = attack * decay * tailFade;

            // Légère modulation vibrato très subtile pour la chaleur analogique
            float tremolo = 1f + 0.025f * Mathf.Sin(twoPi * 2.5f * t);
            float bell = Mathf.Exp(-t * 14f) * 0.06f * Mathf.Sin(twoPi * freq * 3f * t);

            float wave = Mathf.Sin(twoPi * freq * t) * 0.76f
                       + Mathf.Sin(twoPi * freq * 2f * t) * (isBass ? 0.14f : 0.18f)
                       + bell;

            int idx = (startIdx + i) % totalSamples;
            buffer[idx] += wave * env * tremolo * amp;
        }
    }

    // ==================== SYNTHÈSE PROCÉDURALE DES EFFETS SONORES (ADSR DOUX) ====================

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
                float env = u < 0.08f ? (u / 0.08f) : Mathf.Pow(1f - u, 1.5f);
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
