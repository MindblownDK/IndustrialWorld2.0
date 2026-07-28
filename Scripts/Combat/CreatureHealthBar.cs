// Assets/Scripts/VoxelEngine/Combat/CreatureHealthBar.cs
//
// Premium, themed health bar that only appears when the player LOOKS at the creature
// AND is within range. Each creature type configures its own colours + style (no generic
// bars). Features: smooth fill lerp, alpha fade-in/out, scale-in pop, damage flash, and
// a health-based colour gradient — all billboarded to face the camera on any planet.

using UnityEngine;

namespace VoxelEngine.Combat
{
    public class CreatureHealthBar : MonoBehaviour
    {
        [Header("Target")]
        public Damageable target;
        public float showRange = 30f;
        public float heightAbove = 2.2f;

        [Header("Theme (per creature type)")]
        [Tooltip("Bar colour at FULL health.")]
        public Color fillColor = new Color(0.45f, 0.70f, 0.20f);
        [Tooltip("Bar colour at LOW health.")]
        public Color fillColorLow = new Color(0.70f, 0.10f, 0.05f);
        public Color bgColor = new Color(0.06f, 0.06f, 0.06f, 0.85f);
        public Color borderColor = new Color(0.30f, 0.25f, 0.18f, 0.90f);
        public float barWidth = 1.3f;
        public float barHeight = 0.16f;

        private Transform _bar;
        private SpriteRenderer _borderSR, _bgSR, _fillSR;
        private float _alpha;
        private float _targetAlpha;
        private float _displayedFrac = 1f;
        private float _lastFrac = 1f;
        private float _flashT;
        private static Sprite _whiteSprite;

        private static Sprite WhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color32[16];
            for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply();
            _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            return _whiteSprite;
        }

        private SpriteRenderer MakeLayer(string n, Color c, Vector3 pos, Vector2 size)
        {
            var go = new GameObject(n);
            go.transform.SetParent(_bar, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = WhiteSprite();
            sr.color = c;
            return sr;
        }

        private void Awake()
        {
            if (target == null) target = GetComponent<Damageable>();
            _bar = new GameObject("HealthBar").transform;
            _bar.SetParent(transform, false);
            _bar.localPosition = new Vector3(0f, heightAbove, 0f);

            Sprite _ = WhiteSprite(); // init
            _borderSR = MakeLayer("Border", borderColor, new Vector3(0, 0, 0.01f), new Vector2(barWidth + 0.08f, barHeight + 0.08f));
            _bgSR     = MakeLayer("BG",     bgColor,     new Vector3(0, 0, 0f),    new Vector2(barWidth, barHeight));
            _fillSR   = MakeLayer("Fill",   fillColor,   new Vector3(0, 0, -0.01f), new Vector2(barWidth, barHeight));
            _bar.gameObject.SetActive(false); // hidden until looked at
        }

        private void Update()
        {
            if (target == null || _bar == null) return;
            float dt = Time.deltaTime;

            // --- health fraction + smooth fill + damage flash ---
            float frac = target.maxHealth > 0f ? Mathf.Clamp01(target.Health / target.maxHealth) : 0f;
            if (frac < _lastFrac - 0.001f) _flashT = 1f; // flash on damage
            _lastFrac = frac;
            _flashT = Mathf.MoveTowards(_flashT, 0f, dt * 5f);
            _displayedFrac = Mathf.Lerp(_displayedFrac, frac, 1f - Mathf.Exp(-dt * 12f));

            // --- look-at + range gate ---
            Camera cam = Camera.main;
            if (cam == null) { _targetAlpha = 0f; }
            else
            {
                Vector3 toMe = target.transform.position - cam.transform.position;
                float dist = toMe.magnitude;
                if (dist > showRange)
                    _targetAlpha = 0f;
                else if (Physics.Raycast(cam.transform.position, toMe, out var hit, dist + 1f)
                         && hit.collider.GetComponentInParent<Damageable>() == target)
                    _targetAlpha = 1f;
                else
                    _targetAlpha = 0f;
            }
            _alpha = Mathf.Lerp(_alpha, _targetAlpha, 1f - Mathf.Exp(-dt * 10f));

            bool visible = _alpha > 0.02f;
            if (visible != _bar.gameObject.activeSelf) _bar.gameObject.SetActive(visible);
            if (!visible) return;

            // --- fill: anchor-left scale + offset ---
            float w = Mathf.Max(0.001f, barWidth * _displayedFrac);
            _fillSR.transform.localScale = new Vector3(w, barHeight, 1f);
            _fillSR.transform.localPosition = new Vector3(-barWidth * (1f - _displayedFrac) * 0.5f, 0f, -0.01f);

            // --- themed colours (health gradient + flash) + alpha ---
            Color fc = Color.Lerp(fillColorLow, fillColor, _displayedFrac);
            if (_flashT > 0.01f) fc = Color.Lerp(fc, Color.white, _flashT * 0.6f);
            fc.a = _alpha;
            _fillSR.color = fc;

            Color bc = bgColor;     bc.a = bgColor.a * _alpha;       _bgSR.color = bc;
            Color boc = borderColor; boc.a = borderColor.a * _alpha; _borderSR.color = boc;

            // --- scale-in pop ---
            _bar.localScale = Vector3.one * Mathf.Clamp01(_alpha * 1.15f);

            // --- billboard to camera using radial up ---
            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(_bar.position);
            Vector3 fwd = _bar.position - (cam != null ? cam.transform.position : _bar.position + Vector3.forward);
            if (fwd.sqrMagnitude > 0.001f)
                _bar.rotation = Quaternion.LookRotation(fwd.normalized, up);
        }
    }
}
