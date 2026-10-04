// Assets/Scripts/VoxelEngine/UI/SessionNoticeModal.cs
//
// 14.47.1-dev - the modal a player cannot miss.
//
// A kick, a ban or a refusal at the door used to be a toast and a red line
// in the menu - easy to blink past. This is the loud version: a dimmed
// screen, one centered card, one OK button. It lives on its own GameObject
// with its own UIDocument, marked DontDestroyOnLoad, so the modal shown the
// instant the server says goodbye SURVIVES the trip back to the main menu
// and is still standing there when the player arrives.
//
// It never decides anything - it only says what the server already decided.

using UnityEngine;
using UnityEngine.UIElements;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class SessionNoticeModal
    {
        private static GameObject _go;
        private static bool _pushedBlock;

        /// <summary>Show (or replace) the modal. Safe from any scene;
        /// dismissed only by its OK button.</summary>
        public static void Show(string title, string message)
        {
            Dismiss();   // one truth at a time - a newer notice replaces an older one

            var settings = Resources.Load<PanelSettings>("MenuPanelSettings");
            if (settings == null)
            {
                Debug.LogWarning("[SessionNoticeModal] MenuPanelSettings missing from Resources - " +
                                 "falling back to the log only: " + title + " - " + message);
                return;
            }

            _go = new GameObject("SessionNoticeModal");
            Object.DontDestroyOnLoad(_go);
            var doc = _go.AddComponent<UIDocument>();
            doc.panelSettings = settings;
            doc.sortingOrder = 5000;   // above every menu and HUD layer

            var root = doc.rootVisualElement;
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.right = 0;
            root.style.top = 0; root.style.bottom = 0;

            // Dimmed backdrop that swallows clicks - modal means modal.
            var backdrop = new VisualElement();
            backdrop.style.position = Position.Absolute;
            backdrop.style.left = 0; backdrop.style.right = 0;
            backdrop.style.top = 0; backdrop.style.bottom = 0;
            backdrop.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.72f));
            backdrop.style.alignItems = Align.Center;
            backdrop.style.justifyContent = Justify.Center;
            root.Add(backdrop);

            var card = new VisualElement();
            card.style.width = 420;
            card.style.backgroundColor = new StyleColor(new Color(T.BgPanel.r, T.BgPanel.g, T.BgPanel.b, 0.98f));
            card.style.paddingTop = 18;
            card.style.paddingBottom = 18;
            card.style.paddingLeft = 22;
            card.style.paddingRight = 22;
            T.Radius(card, 8);
            T.Border(card, 1, T.AccentRed);
            backdrop.Add(card);

            var titleLabel = T.Title(title ?? "");
            titleLabel.style.color = new StyleColor(T.AccentRed);
            titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(titleLabel);
            card.Add(T.Spacer(10));

            var body = T.Body(message ?? "");
            body.style.whiteSpace = WhiteSpace.Normal;
            body.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(body);
            card.Add(T.Spacer(16));

            var ok = new Button(Dismiss) { text = "OK" };
            ok.style.minHeight = 34;
            ok.style.fontSize = 13;
            ok.style.unityFontStyleAndWeight = FontStyle.Bold;
            ok.style.letterSpacing = 1f;
            ok.style.color = Color.white;
            ok.style.backgroundColor = new StyleColor(new Color(T.AccentRed.r, T.AccentRed.g, T.AccentRed.b, 0.85f));
            T.Radius(ok, T.ButtonRadius);
            T.Border(ok, 0, Color.clear);
            LcdHudTheme.AddMenuInteractions(ok, T.AccentRed,
                new Color(T.AccentRed.r, T.AccentRed.g, T.AccentRed.b, 0.85f));
            card.Add(ok);

            // The player must be able to CLICK the button: free the cursor
            // and block world tools while the modal stands. In the menus the
            // cursor is free already and the block is a harmless no-op.
            // (Cursor fully qualified: UIElements has its own Cursor type.)
            UIState.PushBlock();
            _pushedBlock = true;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
        }

        public static void Dismiss()
        {
            if (_pushedBlock) { UIState.PopBlock(); _pushedBlock = false; }
            if (_go != null) { Object.Destroy(_go); _go = null; }
        }
    }
}
