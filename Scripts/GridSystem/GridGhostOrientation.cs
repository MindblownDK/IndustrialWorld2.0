// Assets/Scripts/VoxelEngine/GridSystem/GridGhostOrientation.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║              WHICH WAY IS THE FRONT? (17.35.1-dev)               ║
// ║                                                                  ║
// ║  A screen has a face. A thruster has a bell. A door has a hinge  ║
// ║  side. Every one of them is placed by pointing a ghost at a      ║
// ║  surface and turning the mouse wheel until something feels       ║
// ║  right - which is fine for a crate and wrong for a panel you     ║
// ║  will read from one side only.                                   ║
// ║                                                                  ║
// ║  The marker is deliberately drawn in the WORLD, on the ghost     ║
// ║  itself, rather than as a HUD arrow: what a player needs to see  ║
// ║  is which face of the object they are about to commit, at the    ║
// ║  moment they commit it. A box says FRONT on that face, and       ║
// ║  three chevrons above it say which edge is up.                   ║
// ║                                                                  ║
// ║  It is not part of any prefab: it is added to the throwaway      ║
// ║  preview object and dies with it, so a saved construct can       ║
// ║  never inherit one.                                             ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using VoxelEngine.Rendering;

namespace VoxelEngine.GridSystem
{
    /// <summary>
    /// Draws "FRONT" and an up indicator on the front face of a placement ghost. Add it to a
    /// preview object with <see cref="Attach"/>; nothing else in the project needs to know.
    /// </summary>
    public class GridGhostOrientation : MonoBehaviour
    {
        /// <summary>The child the marker lives under, so it can be found and refreshed.</summary>
        private const string RootName = "GhostOrientationMarker";

        /// <summary>How far proud of the face the marker floats, in metres.</summary>
        private const float FaceOffset = 0.02f;

        /// <summary>The accent every marker uses: the same cyan the screens themselves glow.</summary>
        private static readonly Color MarkerColor = new Color(0.18f, 0.72f, 0.88f, 1f);
        private static readonly Color BoxColor = new Color(0.10f, 0.45f, 0.60f, 0.42f);

        private static Material _boxMaterial;

        /// <summary>The marker is sized from the ghost's own renderers, which do not change
        /// while a ghost lives - so the walk happens once per ghost, not once per frame.</summary>
        private bool _laidOut;

        // ── Public surface ──────────────────────────────────────────────────────

        /// <summary>
        /// Show (or hide) the front marker on a ghost. Safe to call every frame: the marker is
        /// built once per ghost and only repositioned after that.
        /// </summary>
        /// <param name="ghost">The preview object the builder instantiated.</param>
        /// <param name="show">False hides it without destroying it, for ghosts that have no front.</param>
        public static void Attach(GameObject ghost, bool show)
        {
            if (ghost == null) return;

            Transform marker = ghost.transform.Find(RootName);
            if (!show)
            {
                if (marker != null && marker.gameObject.activeSelf) marker.gameObject.SetActive(false);
                return;
            }

            if (marker == null)
            {
                GameObject root = new GameObject(RootName);
                root.transform.SetParent(ghost.transform, false);
                root.AddComponent<GridGhostOrientation>();
                marker = root.transform;
            }

            if (!marker.gameObject.activeSelf) marker.gameObject.SetActive(true);

            var orientation = marker.GetComponent<GridGhostOrientation>();
            if (orientation != null && orientation._laidOut) return;

            Layout(marker, ghost);
            if (orientation != null) orientation._laidOut = true;
        }

        /// <summary>
        /// True when this renderer belongs to a marker. The ghost builder tints every renderer
        /// it finds with its own preview material; the marker has to survive that or it turns
        /// into another translucent copy of the block and says nothing.
        /// </summary>
        public static bool IsMarker(Renderer renderer)
            => renderer != null && renderer.GetComponentInParent<GridGhostOrientation>() != null;

        // ── Building and sizing ─────────────────────────────────────────────────

        private static void Layout(Transform marker, GameObject ghost)
        {
            if (!TryLocalBounds(ghost, out Bounds bounds)) return;
            if (bounds.size.x <= 0.001f || bounds.size.y <= 0.001f) return;

            // The ghost's own local space: a block is built facing -Z, so its front face is
            // the minimum Z of its renderers.
            marker.localPosition = new Vector3(bounds.center.x, bounds.center.y, bounds.min.z - FaceOffset);
            marker.localRotation = Quaternion.identity;
            marker.localScale = Vector3.one;

            float width = bounds.size.x;
            float height = bounds.size.y;

            // The box: a plate across the middle of the face, big enough to read from a few
            // metres away and small enough that the block is still visible around it.
            Transform box = marker.Find("Box");
            if (box == null) box = BuildBox(marker.transform);
            Vector3 boxSize = new Vector3(width * 0.62f, height * 0.26f, 1f);
            box.localScale = boxSize;

            Transform label = marker.Find("Label");
            if (label == null) label = BuildText(marker.transform, "Label", "FRONT", 64);
            FitTo(label.GetComponent<TextMesh>(), boxSize.x * 0.80f, boxSize.y * 0.74f);
            label.localPosition = new Vector3(0f, 0f, -0.004f);

            // Three chevrons above the box: the one thing a head-on view of a flat panel
            // cannot tell you is which way is up, and a screen mounted upside down is a very
            // expensive mistake to discover after the fact.
            Transform arrows = marker.Find("Arrows");
            if (arrows == null) arrows = BuildText(marker.transform, "Arrows", "\u25B2\u25B2\u25B2", 64);
            float arrowHeight = height * 0.10f;
            FitTo(arrows.GetComponent<TextMesh>(), width * 0.46f, arrowHeight);
            arrows.localPosition = new Vector3(0f, boxSize.y * 0.5f + arrowHeight * 0.85f, -0.004f);
        }

        private static Transform BuildBox(Transform parent)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Box";
            go.transform.SetParent(parent, false);
            // A quad's face points +Z; the block's front is -Z, so turn it round.
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            go.transform.localPosition = Vector3.zero;

            var collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = BoxMaterial();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            return go.transform;
        }

        private static Transform BuildText(Transform parent, string name, string text, int fontSize)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.fontSize = fontSize;
            mesh.characterSize = 0.1f;
            mesh.color = MarkerColor;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.fontStyle = FontStyle.Bold;
            WorldTextMaterial.Apply(mesh);

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            return go.transform;
        }

        /// <summary>Scale a finished TextMesh so its measured mesh fits a box. Same rule the
        /// screens use: measure what the font actually produced rather than trusting a
        /// constant about how many metres a character is.</summary>
        private static void FitTo(TextMesh mesh, float maxWidth, float maxHeight)
        {
            if (mesh == null) return;
            var filter = mesh.GetComponent<MeshFilter>();
            Mesh built = filter != null ? filter.sharedMesh : null;
            if (built == null) return;

            Bounds bounds = built.bounds;
            if (bounds.size.x <= 0.0001f || bounds.size.y <= 0.0001f) return;

            float scale = Mathf.Min(maxWidth / bounds.size.x, maxHeight / bounds.size.y);
            mesh.transform.localScale = Vector3.one * Mathf.Max(0.0001f, scale);
        }

        private static Material BoxMaterial()
        {
            if (_boxMaterial != null) return _boxMaterial;
            _boxMaterial = RuntimeMaterials.MakeTranslucentLit(BoxColor);
            _boxMaterial.name = "GhostOrientation_Box";
            if (_boxMaterial.HasProperty("_EmissionColor"))
            {
                _boxMaterial.EnableKeyword("_EMISSION");
                _boxMaterial.SetColor("_EmissionColor", new Color(MarkerColor.r, MarkerColor.g, MarkerColor.b) * 0.35f);
            }
            return _boxMaterial;
        }

        /// <summary>
        /// The ghost's own bounds, in the ghost's local space, computed from its renderers
        /// rather than from the item's grid size: a screen is 12 percent smaller than its
        /// cell, and the marker has to sit on the glass, not on the cell wall.
        /// </summary>
        private static bool TryLocalBounds(GameObject ghost, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            Matrix4x4 toRoot = ghost.transform.worldToLocalMatrix;

            // Active renderers only. A cockpit ships both shells inside one prefab with
            // one of them switched off; measuring the hidden hull would put the marker on
            // a nose that the player is not looking at.
            var renderers = ghost.GetComponentsInChildren<Renderer>(false);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || IsMarker(r)) continue;

                Bounds local = r.localBounds;
                Vector3 extents = local.extents;
                if (extents.sqrMagnitude <= 0f) continue;

                Matrix4x4 m = toRoot * r.transform.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    var sign = new Vector3(
                        (corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f);
                    Vector3 point = m.MultiplyPoint3x4(local.center + Vector3.Scale(extents, sign));
                    if (!any) { bounds = new Bounds(point, Vector3.zero); any = true; }
                    else bounds.Encapsulate(point);
                }
            }

            return any;
        }
    }
}
