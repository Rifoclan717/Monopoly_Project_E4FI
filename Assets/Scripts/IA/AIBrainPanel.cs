using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CampusTycoon.AI
{
    /// <summary>
    /// Panneau « cerveau de l'IA » (mode jury) : touche [I] pour l'afficher ou le masquer.
    /// Montre, pour chaque IA, ses réglages de personnalité et ses dernières décisions
    /// (action, score, justification), au moment où elles sont prises.
    ///
    /// S'ajoute tout seul au lancement du jeu : aucune scène à modifier.
    /// Ne lit que AIController.DecisionMade : il n'influence jamais la partie.
    /// </summary>
    public class AIBrainPanel : MonoBehaviour
    {
        [Tooltip("Panneau visible au lancement (sinon : touche [I])")]
        [SerializeField] private bool visible;
        [Tooltip("Petit rappel « [I] Cerveau de l'IA » en bas à droite quand le panneau est masqué")]
        [SerializeField] private bool showHint = true;
        [Tooltip("Nombre de décisions affichées par IA")]
        [Range(1, 10)] [SerializeField] private int decisionsShown = 4;
        [Tooltip("Taille du panneau (1 = taille normale en 1080p)")]
        [Range(0.6f, 2f)] [SerializeField] private float sizeFactor = 1f;
        [Tooltip("Distance entre le haut de l'écran et le panneau, en pixels pour un écran 1080p (sous la barre d'outils du HUD)")]
        [Min(0f)] [SerializeField] private float topOffset = 170f;

        private static AIBrainPanel instance;

        private readonly AIDecisionLog log = new AIDecisionLog(10);
        private readonly Dictionary<int, AIController> controllers = new Dictionary<int, AIController>();

        private GUIStyle titleStyle, nameStyle, paramStyle, headlineStyle, reasonStyle, hintStyle, boxStyle;
        private float stylesScale = -1f;

        internal AIDecisionLog Log => log;
        public bool Visible { get => visible; set => visible = value; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            var go = new GameObject("AIBrainPanel (cerveau de l'IA)");
            DontDestroyOnLoad(go);
            go.AddComponent<AIBrainPanel>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
        }

        private void OnEnable()
        {
            AIController.DecisionMade += Record;
            CampusEvents.OnTurnStarted += HandleTurnStarted;
        }

        private void OnDisable()
        {
            AIController.DecisionMade -= Record;
            CampusEvents.OnTurnStarted -= HandleTurnStarted;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.iKey.wasPressedThisFrame) visible = !visible;
        }

        // ------------------------------------------------------------------
        // Collecte
        // ------------------------------------------------------------------

        private void Record(AIController controller, AIDecision decision)
        {
            if (controller == null || decision == null) return;
            int index = controller.PlayerIndex;

            // Nouvelle partie (nouveau contrôleur pour ce joueur) : on repart d'un journal vide.
            if (!controllers.TryGetValue(index, out var known) || known != controller)
            {
                log.ClearPlayer(index);
                controllers[index] = controller;
            }
            log.Add(index, controller.Character, decision);
        }

        /// <summary>Premier tour du joueur 0 : une nouvelle partie commence.</summary>
        private void HandleTurnStarted(int playerId, int turnNumber, int maxTurns)
        {
            if (playerId != 0 || turnNumber != 1) return;
            log.Clear();
            controllers.Clear();
        }

        // ------------------------------------------------------------------
        // Affichage (IMGUI : aucun Canvas à configurer, dessiné par-dessus le HUD)
        // ------------------------------------------------------------------

        private void OnGUI()
        {
            float scale = Mathf.Clamp(Screen.height / 1080f, 0.75f, 1.6f) * sizeFactor;
            EnsureStyles(scale);

            if (!visible)
            {
                if (showHint && log.LastSequence > 0)
                    GUI.Label(new Rect(Screen.width - 200f * scale, Screen.height - 30f * scale, 190f * scale, 24f * scale),
                        "[I] Cerveau de l'IA", hintStyle);
                return;
            }

            float width = 470f * scale;
            float x = Screen.width - width - 14f * scale;
            float y = topOffset * scale;
            GUILayout.BeginArea(new Rect(x, y, width, Screen.height - y - 20f * scale));
            GUILayout.BeginVertical(boxStyle);

            GUILayout.Label("<b>Cerveau de l'IA</b>   <size=" + Mathf.RoundToInt(11f * scale) + "><color=#9aa4b2>[I] masquer</color></size>", titleStyle);
            GUILayout.Label("Chaque action reçoit un score d'utilité : l'IA garde la meilleure, puis applique sa personnalité.", reasonStyle);

            bool any = false;
            foreach (int player in log.Players)
            {
                any = true;
                GUILayout.Space(8f * scale);
                DrawPlayer(player);
            }

            if (!any)
            {
                GUILayout.Space(6f * scale);
                GUILayout.Label("En attente de la première décision d'une IA…", reasonStyle);
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void DrawPlayer(int player)
        {
            CharacterId character = log.CharacterOf(player);
            GUILayout.Label("<color=#ffd166>" + AIBarks.SpeakerName(character) + "</color>  <color=#9aa4b2>(joueur " + player + ")</color>", nameStyle);

            if (controllers.TryGetValue(player, out var controller) && controller != null)
                GUILayout.Label(AIDecisionLog.DescribeParameters(controller.Parameters, controller.Difficulty), paramStyle);

            var entries = log.For(player);
            int shown = Mathf.Min(decisionsShown, entries.Count);
            for (int i = 0; i < shown; i++)
            {
                var entry = entries[i];
                bool latest = i == 0;
                string color = AIDecisionLog.IsActive(entry.Decision.Action) ? "#7ee787" : "#c9d1d9";
                string headline = "<color=" + color + ">" + (latest ? "» " : "   ") + AIDecisionLog.Headline(entry.Decision) + "</color>";
                GUILayout.Label(latest ? "<b>" + headline + "</b>" : headline, headlineStyle);
                if (!string.IsNullOrEmpty(entry.Decision.Reason))
                    GUILayout.Label("      " + entry.Decision.Reason, reasonStyle);
            }
        }

        private void EnsureStyles(float scale)
        {
            if (titleStyle != null && Mathf.Abs(scale - stylesScale) < 0.01f) return;
            stylesScale = scale;

            boxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(Mathf.RoundToInt(12 * scale), Mathf.RoundToInt(12 * scale),
                                         Mathf.RoundToInt(10 * scale), Mathf.RoundToInt(10 * scale))
            };
            titleStyle = Make(16f, scale, Color.white, false);
            nameStyle = Make(14f, scale, Color.white, false);
            paramStyle = Make(11.5f, scale, new Color(0.62f, 0.66f, 0.72f), true);
            headlineStyle = Make(13f, scale, Color.white, false);
            reasonStyle = Make(11.5f, scale, new Color(0.75f, 0.78f, 0.82f), true);
            hintStyle = Make(12f, scale, new Color(1f, 1f, 1f, 0.7f), false);
            hintStyle.alignment = TextAnchor.MiddleRight;
        }

        private static GUIStyle Make(float size, float scale, Color color, bool wrap)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(size * scale),
                richText = true,
                wordWrap = wrap
            };
            style.normal.textColor = color;
            return style;
        }

        // Pour les tests hors Unity.
        internal void EnableForTests() => OnEnable();
        internal void DisableForTests() => OnDisable();
    }
}
