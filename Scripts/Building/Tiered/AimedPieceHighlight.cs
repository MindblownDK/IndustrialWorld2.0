// Assets/Scripts/VoxelEngine/Building/Tiered/AimedPieceHighlight.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║        INDUSTRIAL WORLD — WHAT THE HAMMER IS POINTING AT         ║
// ║                                                                  ║
// ║  With the Building Hammer out and no piece armed, the crosshair  ║
// ║  carries no hologram — it is not promising to build anything.    ║
// ║  What it does instead is MARK: the piece under it is boxed in a  ║
// ║  thin unlit outline, as a hammer marks a mark, so the player can ║
// ║  read what a click would upgrade or repair before pressing it.   ║
// ║                                                                  ║
// ║  The outline is twelve line-topology edges around the piece's    ║
// ║  own renderer bounds — no shader, no material pass, no second    ║
// ║  copy of the model — so it costs one draw call on a piece that   ║
// ║  is already being drawn, and it never occludes anything.         ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Items;
using VoxelEngine.Player;
using VoxelEngine.UI;

namespace VoxelEngine.Building.Tiered
{
    /// <summary>
    /// Marks the tiered piece or static block under the Building Hammer's crosshair.
    /// Reads only: it never damages, upgrades or places anything.
    /// </summary>
    public sealed class AimedPieceHighlight : MonoBehaviour
    {
        public static AimedPieceHighlight Instance { get; private set; }

        /// <summary>The tiered piece the hammer is aimed at, or null.</summary>
        public PlacedTieredBlock AimedPiece { get; private set; }

        /// <summary>The static BlockItem the hammer is aimed at (repairable), or null.</summary>
        public PlacedBlock AimedStaticBlock { get; private set; }

        /// <summary>Colour the outline is currently drawn in - read by the assist card.</summary>
        public Color CurrentColor { get; private set; } = Color.clear;

        // The outline's own three colours. Gold means "a click would change this", teal means
        // "this is repair work", cool grey means "this is already what the hammer is set to".
        private static readonly Color ChangeColor  = new(0.98f, 0.80f, 0.26f, 1f);
        private static readonly Color RepairColor  = new(0.20f, 0.85f, 0.75f, 1f);
        private static readonly Color SettledColor = new(0.62f, 0.68f, 0.78f, 1f);

        private const float OutlineInflate = 0.03f;
        private const float PulseSpeed = 3.4f;

        private static readonly RaycastHit[] s_probe = new RaycastHit[32];

        private GameObject _box;
        private Mesh _mesh;
        private MeshRenderer _meshRenderer;
        private Material _material;

        private readonly Vector3[] _verts = new Vector3[24];
        private readonly Vector3[] _corners = new Vector3[8];
        private Bounds _bounds;
        private bool _hasBounds;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            BuildBox();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_box != null) Destroy(_box);
            if (_mesh != null) Destroy(_mesh);
            if (_material != null) Destroy(_material);
            _box = null; _mesh = null; _material = null; _meshRenderer = null;
        }

        private void LateUpdate()
        {
            AimedPiece = null;
            AimedStaticBlock = null;

            if (!TryResolveAim(out var piece, out var staticBlock, out var bounds))
            {
                Hide();
                return;
            }

            AimedPiece = piece;
            AimedStaticBlock = staticBlock;
            Show(bounds, ColourFor(piece, staticBlock));
        }

        /// <summary>
        /// Finds what the hammer is aimed at, and whether it should be marked at all.
        /// Everything that can stand the outline down is checked here and nowhere else.
        /// </summary>
        private bool TryResolveAim(out PlacedTieredBlock piece, out PlacedBlock staticBlock, out Bounds bounds)
        {
            piece = null;
            staticBlock = null;
            bounds = default;

            var build = BuildSystemV2.Instance;
            if (build == null || build.shootCamera == null) return false;

            // A dial owns the cursor while it is up, and a piece hologram is the promise that
            // a click builds - neither should have an outline drawn over it.
            if (UIState.IsBlocking) return false;
            if (VoxelEngine.UI.RadialWheelController.AnyOpen) return false;
            if (build.GhostShown) return false;

            if (!IsHammerHeld(build)) return false;

            var ray = build.shootCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            if (!TryNearestTarget(ray, Mathf.Max(2f, build.reach), build.transform, out var hit)) return false;

            piece = hit.collider.GetComponentInParent<PlacedTieredBlock>();
            if (piece == null)
                staticBlock = hit.collider.GetComponentInParent<PlacedBlock>();
            if (piece == null && staticBlock == null) return false;

            return TryGetBounds(piece != null ? (Component)piece : staticBlock, out bounds);
        }

        private static bool IsHammerHeld(BuildSystemV2 build)
        {
            var stack = build.inventory != null ? build.inventory.ActiveStack : null;
            return stack != null && !stack.IsEmpty && stack.item is Hammer;
        }

        /// <summary>The nearest collider along the view ray that is not the player or a ghost.</summary>
        private static bool TryNearestTarget(Ray ray, float distance, Transform ownRoot, out RaycastHit hit)
        {
            int count = Physics.RaycastNonAlloc(ray, s_probe, distance, ~0, QueryTriggerInteraction.Ignore);
            hit = default;
            float closest = float.MaxValue;
            bool found = false;
            Transform selfRoot = ownRoot != null ? ownRoot.root : null;

            for (int i = 0; i < count; i++)
            {
                var candidate = s_probe[i];
                if (candidate.collider == null || candidate.distance >= closest) continue;
                if (selfRoot != null && candidate.collider.transform.IsChildOf(selfRoot)) continue;
                if (PlayerRaycastFilter.IsOwnPlayerCollider(candidate.collider, ownRoot)) continue;
                closest = candidate.distance;
                hit = candidate;
                found = true;
            }
            return found;
        }

        /// <summary>
        /// The piece's own drawn extent, with every effect renderer left out so a strut of
        /// steam or a status glow never stretches the box.
        /// </summary>
        private static bool TryGetBounds(Component target, out Bounds bounds)
        {
            bounds = default;
            if (target == null) return false;

            bool have = false;
            var renderers = target.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (r is SkinnedMeshRenderer skinned && skinned.sharedMesh == null) continue;

                if (!have) { bounds = r.bounds; have = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return have;
        }

        private static Color ColourFor(PlacedTieredBlock piece, PlacedBlock staticBlock)
        {
            var wheel = HammerBuildWheel.Instance;
            if (wheel != null && wheel.IsRepairMode) return RepairColor;

            if (piece != null)
            {
                var target = HammerTierSelection.Selected;
                return piece.tier >= target ? SettledColor : ChangeColor;
            }

            // A static block has no tiers: the hammer either repairs it or leaves it alone.
            if (staticBlock != null && staticBlock.Damage01 > 0.001f) return RepairColor;
            return SettledColor;
        }

        private void Show(Bounds bounds, Color colour)
        {
            if (_box == null || _mesh == null || _meshRenderer == null) return;

            if (!_hasBounds || !Approximately(_bounds, bounds))
            {
                _bounds = bounds;
                _hasBounds = true;
                WriteBox(bounds);
            }

            _box.transform.SetPositionAndRotation(bounds.center, Quaternion.identity);
            if (!_box.activeSelf) _box.SetActive(true);

            // A slow breath so the mark reads as "look at me" without strobing.
            float pulse = 0.78f + 0.22f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * PulseSpeed));
            CurrentColor = new Color(colour.r, colour.g, colour.b, colour.a);
            _material.color = new Color(colour.r * pulse, colour.g * pulse, colour.b * pulse, 1f);
            if (_material.HasProperty("_BaseColor"))
                _material.SetColor("_BaseColor", _material.color);
        }

        private void Hide()
        {
            _hasBounds = false;
            CurrentColor = Color.clear;
            if (_box != null && _box.activeSelf) _box.SetActive(false);
        }

        private static bool Approximately(Bounds a, Bounds b)
            => (a.center - b.center).sqrMagnitude < 0.000004f
               && (a.size - b.size).sqrMagnitude < 0.000004f;

        // ══════════════════════════════════════════════════════════════════
        //  GEOMETRY - twelve edges, built once, rewritten only on change
        // ══════════════════════════════════════════════════════════════════

        private void BuildBox()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Unlit/Color")
                         ?? Shader.Find("Sprites/Default");
            _material = new Material(shader) { hideFlags = HideFlags.DontSave };
            _material.color = ChangeColor;
            if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", ChangeColor);
            if (_material.HasProperty("_Cull")) _material.SetFloat("_Cull", 0f);

            _mesh = new Mesh { name = "AimedPieceOutline", hideFlags = HideFlags.DontSave };
            _mesh.MarkDynamic();
            var indices = new int[24];
            for (int i = 0; i < 24; i++) indices[i] = i;
            _mesh.vertices = new Vector3[24];
            _mesh.SetIndices(indices, MeshTopology.Lines, 0);

            // Parented to nothing: the host may sit on a scaled or rotated object, and an
            // outline that inherits that would not hug the piece it marks.
            _box = new GameObject("AimedPieceOutline");
            var filter = _box.AddComponent<MeshFilter>();
            filter.sharedMesh = _mesh;
            _meshRenderer = _box.AddComponent<MeshRenderer>();
            _meshRenderer.sharedMaterial = _material;
            _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
            _meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            _meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _meshRenderer.allowOcclusionWhenDynamic = false;
            _box.SetActive(false);
        }

        /// <summary>Writes the twelve edges of <paramref name="bounds"/> as line pairs, local to
        /// the box's own centre so moving it is one transform write.</summary>
        private void WriteBox(Bounds bounds)
        {
            Vector3 c = bounds.center;
            Vector3 e = bounds.extents + Vector3.one * OutlineInflate;

            // Corner layout: bit 0 = +x, bit 1 = +y, bit 2 = +z. Stored local to the box
            // centre, because the box object is moved rather than the vertices.
            for (int corner = 0; corner < 8; corner++)
            {
                _corners[corner] = new Vector3(
                    ((corner & 1) != 0 ? e.x : -e.x),
                    ((corner & 2) != 0 ? e.y : -e.y),
                    ((corner & 4) != 0 ? e.z : -e.z)) - c;
                _verts[corner] = _corners[corner];
            }

            // An edge joins two corners differing in exactly one bit. Line topology needs the
            // endpoints as separate vertices, so each edge is written as its own pair.
            int v = 8;
            for (int bit = 0; bit < 3; bit++)
            {
                int mask = 1 << bit;
                for (int corner = 0; corner < 8; corner++)
                {
                    if ((corner & mask) != 0) continue;
                    _verts[v++] = _corners[corner];
                    _verts[v++] = _corners[corner | mask];
                }
            }

            _mesh.vertices = _verts;
            _mesh.RecalculateBounds();
        }
    }
}
