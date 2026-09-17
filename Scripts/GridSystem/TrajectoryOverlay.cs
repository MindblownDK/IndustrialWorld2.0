// Assets/Scripts/VoxelEngine/GridSystem/TrajectoryOverlay.cs
//
// Draws the predicted flight path for the grid the local player is piloting.
//
// The path itself is solved by TrajectoryPredictor; this file is presentation only.
// It owns one LineRenderer and one impact marker for the whole game, not a GameObject
// per point, and both are hidden rather than destroyed when the overlay is off.
//
// Gating, in order: the toggle must be on, the player must be piloting, and the camera
// must be in the WIDE exterior view (the second zoom-out). That last rule is what keeps
// the prediction out of the way during ordinary first-person flying.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Settings;
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.GridSystem
{
    public static class TrajectoryOverlay
    {
        // ── Toggle state ─────────────────────────────────────────────────────────
        private const string EnabledKey = "ve.trajectoryCamera";

        /// <summary>Player-facing toggle, persisted alongside the other settings.</summary>
        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(EnabledKey, 1) != 0;
            set { PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>True while the path is actually on screen. Drives the HUD readout.</summary>
        public static bool IsShowing { get; private set; }

        /// <summary>The most recent solution, for the HUD to describe in text.</summary>
        public static TrajectorySolution Solution { get; private set; }

        // ── Visuals ──────────────────────────────────────────────────────────────
        private static GameObject _root;
        private static LineRenderer _line;
        private static Transform _marker;
        private static MeshRenderer _markerRenderer;
        private static Material _lineMaterial;
        private static Material _markerMaterial;
        private static MaterialPropertyBlock _markerProps;
        private static Vector3[] _buffer = new Vector3[64];

        private static readonly Color ImpactColor = new(0.95f, 0.26f, 0.22f);
        private static readonly Color OrbitColor = new(0.30f, 0.85f, 0.50f);
        private static readonly Color EscapeColor = new(0.70f, 0.55f, 1.00f);
        private static readonly Color ClearColor = new(0.42f, 0.72f, 0.95f);

        // ── Per-frame entry (called from GridPilotHud.Tick) ───────────────────────
        public static void Tick()
        {
            bool blocked = VoxelEngine.UI.UIState.IsBlocking
                || VoxelEngine.UI.UIState.IsHardPause
                || VoxelEngine.UI.UIState.TextInputActive;

            if (!blocked && GameSettings.WasPressed(InputAction.TrajectoryCamera))
            {
                Enabled = !Enabled;
                VoxelEngine.UI.BuildFeedbackHud.Show(Enabled
                    ? "Trajectory camera ON — zoom out twice to see the predicted path"
                    : "Trajectory camera OFF");
            }

            var cockpit = GridCockpit.ActivePilotSeat;
            bool active = Enabled
                && !blocked
                && cockpit != null
                && cockpit.Pilot != null
                && cockpit.Grid != null
                && cockpit.IsWideExteriorView;

            if (!active)
            {
                IsShowing = false;
                Solution = default;
                Hide();
                return;
            }

            TrajectorySolution solution = TrajectoryPredictor.Solve(cockpit.Grid);
            Solution = solution;

            var points = TrajectoryPredictor.Points;
            if (solution.Outcome == TrajectoryOutcome.None || points.Count < 2)
            {
                IsShowing = false;
                Hide();
                return;
            }

            IsShowing = true;
            Draw(points, solution);
        }

        /// <summary>Tears the overlay down — called when the pilot leaves the seat.</summary>
        public static void Hide()
        {
            if (_root != null && _root.activeSelf) _root.SetActive(false);
        }

        // ── Rendering ────────────────────────────────────────────────────────────
        private static void Draw(IReadOnlyList<Vector3> points, TrajectorySolution solution)
        {
            if (!EnsureVisuals()) return;
            _root.SetActive(true);

            Color color = solution.Outcome switch
            {
                TrajectoryOutcome.Impact => ImpactColor,
                TrajectoryOutcome.Orbit => OrbitColor,
                TrajectoryOutcome.Escape => EscapeColor,
                _ => ClearColor,
            };

            int count = points.Count;
            if (_buffer.Length < count) _buffer = new Vector3[Mathf.NextPowerOfTwo(count)];
            for (int i = 0; i < count; i++) _buffer[i] = points[i];

            _line.positionCount = count;
            _line.SetPositions(_buffer);
            _line.startColor = new Color(color.r, color.g, color.b, 0.95f);
            // Fade the far end: the prediction is least trustworthy the further out it runs,
            // so the line should not claim equal confidence along its whole length.
            _line.endColor = new Color(color.r, color.g, color.b, 0.22f);

            if (solution.HasImpact)
            {
                _marker.gameObject.SetActive(true);
                _marker.position = solution.ImpactPoint + solution.ImpactNormal * 0.05f;
                _marker.rotation = Quaternion.LookRotation(solution.ImpactNormal);

                // Keep the marker a constant size on screen. A disc sized in world units is
                // invisible from 500 m up, which is exactly when the pilot needs to see it.
                var cam = Camera.main;
                float scale = 1.6f;
                if (cam != null)
                    scale = Mathf.Clamp(Vector3.Distance(cam.transform.position, solution.ImpactPoint) * 0.035f, 0.8f, 30f);

                // Pulse so the impact point reads as a warning rather than scenery.
                scale *= 1f + Mathf.Sin(Time.unscaledTime * 6f) * 0.10f;
                _marker.localScale = new Vector3(scale, scale, scale);

                _markerProps ??= new MaterialPropertyBlock();
                _markerRenderer.GetPropertyBlock(_markerProps);
                _markerProps.SetColor("_BaseColor", color);
                _markerProps.SetColor("_Color", color);
                _markerRenderer.SetPropertyBlock(_markerProps);
            }
            else if (_marker.gameObject.activeSelf)
            {
                _marker.gameObject.SetActive(false);
            }
        }

        private static bool EnsureVisuals()
        {
            if (_root != null) return true;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader == null) return false;

            _lineMaterial = new Material(shader) { name = "TrajectoryPathRuntime", hideFlags = HideFlags.DontSave };
            MakeTransparent(_lineMaterial);
            _markerMaterial = new Material(shader) { name = "TrajectoryImpactRuntime", hideFlags = HideFlags.DontSave };
            MakeTransparent(_markerMaterial);

            _root = new GameObject("TrajectoryOverlayRuntime") { hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(_root);

            _line = _root.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.sharedMaterial = _lineMaterial;
            _line.startWidth = 0.22f;
            _line.endWidth = 0.10f;
            _line.numCapVertices = 2;
            _line.numCornerVertices = 2;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.alignment = LineAlignment.View;

            var marker = GameObject.CreatePrimitive(PrimitiveType.Quad);
            marker.name = "TrajectoryImpactMarker";
            marker.hideFlags = HideFlags.DontSave;
            // A marker with a collider would be hit by the very raycasts that produced it.
            var markerCollider = marker.GetComponent<Collider>();
            if (markerCollider != null) Object.Destroy(markerCollider);
            marker.transform.SetParent(_root.transform, false);
            _markerRenderer = marker.GetComponent<MeshRenderer>();
            _markerRenderer.sharedMaterial = _markerMaterial;
            _markerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _markerRenderer.receiveShadows = false;
            _marker = marker.transform;
            marker.SetActive(false);

            return true;
        }

        private static void MakeTransparent(Material m)
        {
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3000;
        }

        /// <summary>Short status line for the cockpit HUD.</summary>
        public static string StatusText()
        {
            if (!Enabled) return "TRAJECTORY OFF";
            if (!IsShowing) return "TRAJECTORY · ZOOM OUT";
            return Solution.Outcome switch
            {
                TrajectoryOutcome.Impact => $"IMPACT IN {Solution.TimeToImpact:0.0}s · {Solution.ImpactSpeed:0} m/s",
                TrajectoryOutcome.Orbit => "PATH CLEAR · WILL ORBIT",
                TrajectoryOutcome.Escape => "PATH CLEAR · LEAVING WELL",
                TrajectoryOutcome.Clear => "PATH CLEAR",
                _ => "TRAJECTORY · NO SOLUTION",
            };
        }
    }
}
