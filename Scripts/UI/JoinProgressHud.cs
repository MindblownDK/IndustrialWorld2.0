// Assets/Scripts/VoxelEngine/UI/JoinProgressHud.cs
//
// 14.23.0-dev - milestone 8, step 1: join a game, not a save file.
//
// A client that joined from the main menu arrives in a game scene with NO
// world in it: generation is held until the host's world card lands. Without
// something on screen that is a black room and a player wondering whether the
// game crashed. This overlay owns those seconds - it says what is happening,
// and when it goes wrong it says what went wrong and offers the way back
// instead of leaving the player stranded.
//
// It mounts itself the moment the scene loads and only while WorldBootGate is
// held, so in single player this file never draws a single pixel.

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VoxelEngine.Menu;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    [DisallowMultipleComponent]
    public class JoinProgressHud : MonoBehaviour
    {
        public static JoinProgressHud Instance { get; private set; }

        /// <summary>Scene to fall back to when the join fails. Read from the
        /// pause menu's own setting when one is in the scene, so renaming the
        /// menu scene stays a one-place change.</summary>
        private const string DefaultMenuScene = "MainMenu";

        private const float DotPeriod = 0.45f;   // spinner cadence, seconds per step
        private const float FadeSeconds = 0.22f; // EaseOutCubic entry, per the UI guidelines

        private UIDocument _doc;
        private PanelSettings _runtimePanel;
        private VisualElement _overlay;
        private VisualElement _card;
        private Label _title;
        private Label _status;
        private Label _spinner;
        private Button _backBtn;

        private float _fadeStartedAt = -1f;
        private int _dotStep;
        private float _nextDotAt;
        private bool _failureShown;

        /// <summary>Created automatically on EVERY scene load, and only when a
        /// join is actually in flight. No prefab, no setup step, nothing for a
        /// solo world to carry.
        ///
        /// The subscription is what matters here: RuntimeInitializeOnLoadMethod
        /// fires once, after the FIRST scene of the run - which is the main
        /// menu, where no join is pending. Mounting from it directly meant the
        /// overlay never appeared in the game scene, which is exactly the empty
        /// screen a joining player was left staring at.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallHook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(default, default);   // cover the scene already loaded
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!WorldBootGate.IsHeld) return;
            if (Instance != null) return;
            var go = new GameObject("JoinProgressHud");
            go.AddComponent<JoinProgressHud>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_runtimePanel != null) Destroy(_runtimePanel);
        }

        private void Start()
        {
            if (!WorldBootGate.IsHeld) { Destroy(gameObject); return; }
            Build();
        }

        private void Update()
        {
            if (_overlay == null) return;

            // Entry fade: EaseOutCubic, matching every other panel in the game.
            if (_fadeStartedAt >= 0f)
            {
                float t = Mathf.Clamp01((Time.unscaledTime - _fadeStartedAt) / FadeSeconds);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                _overlay.style.opacity = eased;
                _card.style.translate = new StyleTranslate(new Translate(0, Length.Percent((1f - eased) * -3f)));
                if (t >= 1f) _fadeStartedAt = -1f;
            }

            bool failed = !string.IsNullOrEmpty(WorldBootGate.Failure);

            if (!WorldBootGate.IsHeld && !failed)
            {
                // The world arrived. Nothing left to narrate.
                Destroy(gameObject);
                return;
            }

            _status.text = WorldBootGate.Status;

            if (failed && !_failureShown)
            {
                _failureShown = true;
                _title.text = "COULD NOT JOIN";
                _title.style.color = new StyleColor(T.AccentRed);
                _spinner.style.display = DisplayStyle.None;
                _backBtn.style.display = DisplayStyle.Flex;
                return;
            }

            if (failed) return;

            if (Time.unscaledTime >= _nextDotAt)
            {
                _nextDotAt = Time.unscaledTime + DotPeriod;
                _dotStep = (_dotStep + 1) % 4;
                _spinner.text = new string('.', _dotStep);
            }
        }

        // ─────────────────────────── construction ───────────────────────────

        private void Build()
        {
            _doc = EnsureDocument();
            if (_doc == null) return;

            var root = _doc.rootVisualElement;
            if (root == null) return;

            _overlay = new VisualElement { name = "JoinProgressOverlay" };
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0;
            _overlay.style.top = 0;
            _overlay.style.right = 0;
            _overlay.style.bottom = 0;
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;
            _overlay.style.backgroundColor = new StyleColor(new Color(0.02f, 0.03f, 0.04f, 0.97f));
            _overlay.style.opacity = 0f;
            root.Add(_overlay);

            _card = new VisualElement();
            _card.style.width = 460;
            _card.style.paddingLeft = 26;
            _card.style.paddingRight = 26;
            _card.style.paddingTop = 22;
            _card.style.paddingBottom = 22;
            _card.style.backgroundColor = new StyleColor(T.BgPanel);
            _card.style.alignItems = Align.Center;
            T.Border(_card, 1, T.AccentCyan);
            _overlay.Add(_card);

            _title = new Label("JOINING");
            _title.style.color = new StyleColor(T.AccentCyan);
            _title.style.fontSize = 20;
            _title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _title.style.letterSpacing = 4f;
            _card.Add(_title);

            _card.Add(T.Spacer(10));
            _card.Add(T.AccentDivider());
            _card.Add(T.Spacer(14));

            _status = T.Body(WorldBootGate.Status);
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.unityTextAlign = TextAnchor.MiddleCenter;
            _card.Add(_status);

            _spinner = T.Muted("");
            _spinner.style.fontSize = 22;
            _spinner.style.letterSpacing = 6f;
            _spinner.style.unityTextAlign = TextAnchor.MiddleCenter;
            _spinner.style.height = 26;
            _card.Add(_spinner);

            _backBtn = new Button(ReturnToMenu) { text = "BACK TO MENU" };
            _backBtn.style.display = DisplayStyle.None;
            _backBtn.style.marginTop = 12;
            _backBtn.style.height = 38;
            _backBtn.style.width = 220;
            _backBtn.style.color = new StyleColor(T.TextPrimary);
            _backBtn.style.backgroundColor = new StyleColor(T.BgSlot);
            _backBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            _backBtn.style.letterSpacing = 2f;
            T.Border(_backBtn, 1, T.AccentCyan);
            _backBtn.RegisterCallback<MouseEnterEvent>(_ => _backBtn.style.scale = new StyleScale(new Scale(new Vector2(1.03f, 1.03f))));
            _backBtn.RegisterCallback<MouseLeaveEvent>(_ => _backBtn.style.scale = new StyleScale(new Scale(Vector2.one)));
            _card.Add(_backBtn);

            _fadeStartedAt = Time.unscaledTime;
            _nextDotAt = Time.unscaledTime + DotPeriod;

            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
        }

        /// <summary>Draw on the game's own UI document when there is one, and
        /// otherwise stand up a private panel - the join screen must appear
        /// even when the rest of the HUD has not come up yet, because "not up
        /// yet" is exactly the state it exists to describe.</summary>
        private UIDocument EnsureDocument()
        {
            var controller = GameUIController.Instance;
            if (controller != null)
            {
                var existing = controller.GetComponent<UIDocument>();
                if (existing != null && existing.rootVisualElement != null) return existing;
            }

            var any = FindAnyObjectByType<UIDocument>();
            if (any != null && any.rootVisualElement != null) return any;

            var own = gameObject.AddComponent<UIDocument>();
            _runtimePanel = ScriptableObject.CreateInstance<PanelSettings>();
            _runtimePanel.name = "JoinProgress_RuntimePanelSettings";
            VoxelEngine.Settings.GameSettings.ApplyUiScaleAndFit(_runtimePanel);
            var theme = Resources.Load<ThemeStyleSheet>("MenuTheme")
                        ?? Resources.Load<ThemeStyleSheet>("UnityDefaultRuntimeTheme");
            if (theme != null) _runtimePanel.themeStyleSheet = theme;
            own.panelSettings = _runtimePanel;
            own.sortingOrder = 1000f;   // above every gameplay layer
            return own;
        }

        /// <summary>Abandon the join and go home. The gate is dropped first so
        /// the menu scene cannot inherit a held world.</summary>
        private void ReturnToMenu()
        {
            var bootstrap = VoxelEngine.Networking.NetworkBootstrap.Instance;
            if (bootstrap != null) bootstrap.StopSession();

            var session = WorldSession.Instance;
            if (session != null) session.ClearRemoteJoin();
            else WorldBootGate.Reset();

            UIState.ClearSceneBlocks();
            Time.timeScale = 1f;
            var pause = FindAnyObjectByType<VoxelEngine.Menu.InGamePauseMenu>(FindObjectsInactive.Include);
            string menuScene = pause != null && !string.IsNullOrEmpty(pause.mainMenuScene)
                ? pause.mainMenuScene : DefaultMenuScene;

            try { SceneManager.LoadScene(menuScene); }
            catch (System.Exception ex)
            {
                Debug.LogError("[JoinProgressHud] Could not return to the menu: " + ex.Message);
            }
        }
    }
}
