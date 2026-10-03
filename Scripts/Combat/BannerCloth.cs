// Assets/Scripts/VoxelEngine/Combat/BannerCloth.cs
//
// 14.37.0-dev - THE banner visual, built once in code and reused by every
// 3D display site (placed banner block, grid banner block). The FRAME is
// always identical - gold pole, gold crossbar, gold cross finial - only
// the cloth image and the three text lines vary, and those come from
// TeamBannerRegistry per team. The component subscribes to the registry
// and repaints live when the team's banner is edited.
//
// The cloth is a swallow-tail pennant: a custom 5-vertex mesh (plus a
// duplicated reversed set so both sides render with correct normals),
// UV-mapped straight onto the 256x384 cloth texture. No transparency
// tricks - the notch is geometry.

using UnityEngine;
using VoxelEngine.Networking;

namespace VoxelEngine.Combat
{
    public class BannerCloth : MonoBehaviour
    {
        [Header("Cloth dimensions (meters)")]
        public float clothWidth = 0.84f;
        public float clothHeight = 1.30f;
        [Tooltip("Depth of the swallow-tail notch as a fraction of cloth height.")]
        [Range(0.05f, 0.4f)] public float notchDepth01 = 0.18f;
        public float poleHeight = 2.30f;

        private string _teamId = "";
        private bool _built;
        private Material _clothMaterial;
        private TextMesh _textTop, _textMiddle, _textBottom;

        /// <summary>Show this team's banner (empty/unknown id = default emblem).</summary>
        public void Bind(string teamId)
        {
            _teamId = teamId ?? "";
            if (!_built) Build();
            Repaint();
        }

        public string BoundTeamId => _teamId;

        private void OnEnable()
        {
            TeamBannerRegistry.OnBannerChanged += OnBannerChanged;
            if (_built) Repaint();
        }

        private void OnDisable()
        {
            TeamBannerRegistry.OnBannerChanged -= OnBannerChanged;
        }

        private void OnBannerChanged(string teamId)
        {
            if (!_built) return;
            if (string.IsNullOrEmpty(teamId) || teamId == _teamId) Repaint();
        }

        private void Repaint()
        {
            if (_clothMaterial != null)
            {
                var tex = TeamBannerRegistry.ClothTexture(_teamId);
                _clothMaterial.mainTexture = tex;
                if (_clothMaterial.HasProperty("_BaseMap")) _clothMaterial.SetTexture("_BaseMap", tex);
                if (_clothMaterial.HasProperty("_MainTex")) _clothMaterial.SetTexture("_MainTex", tex);
            }
            TeamBannerRegistry.TextsOf(_teamId, out string top, out string middle, out string bottom);
            if (_textTop != null) _textTop.text = top;
            if (_textMiddle != null) _textMiddle.text = middle;
            if (_textBottom != null) _textBottom.text = bottom;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Construction
        // ─────────────────────────────────────────────────────────────────

        private void Build()
        {
            _built = true;
            var gold = MakeMaterial(new Color(0.85f, 0.68f, 0.21f), metallic: 0.75f, smoothness: 0.62f);

            // Pole: base at local origin, finial on top.
            float poleR = 0.035f;
            AddPart(PrimitiveType.Cylinder, gold, new Vector3(0f, poleHeight * 0.5f, 0f),
                Vector3.zero, new Vector3(poleR * 2f, poleHeight * 0.5f, poleR * 2f), "Banner_Pole");

            // Crossbar the cloth hangs from, just under the finial.
            float barY = poleHeight - 0.16f;
            float barLen = clothWidth + 0.16f;
            AddPart(PrimitiveType.Cylinder, gold, new Vector3(0f, barY, 0f),
                new Vector3(0f, 0f, 90f), new Vector3(0.05f, barLen * 0.5f, 0.05f), "Banner_Crossbar");
            AddPart(PrimitiveType.Sphere, gold, new Vector3(-barLen * 0.5f, barY, 0f),
                Vector3.zero, Vector3.one * 0.085f, "Banner_BarTipL");
            AddPart(PrimitiveType.Sphere, gold, new Vector3(barLen * 0.5f, barY, 0f),
                Vector3.zero, Vector3.one * 0.085f, "Banner_BarTipR");

            // Cross finial above the crossbar - the emblem of the crusade.
            float crossY = poleHeight + 0.10f;
            AddPart(PrimitiveType.Cube, gold, new Vector3(0f, crossY, 0f),
                Vector3.zero, new Vector3(0.045f, 0.26f, 0.045f), "Banner_CrossV");
            AddPart(PrimitiveType.Cube, gold, new Vector3(0f, crossY + 0.045f, 0f),
                Vector3.zero, new Vector3(0.17f, 0.045f, 0.045f), "Banner_CrossH");

            BuildCloth(barY - 0.045f);
            BuildTexts(barY - 0.045f);
            Repaint();
        }

        private void BuildCloth(float topY)
        {
            var go = new GameObject("Banner_Cloth");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, topY, 0f);

            float w = clothWidth, h = clothHeight, notch = h * notchDepth01;

            // Front face (seen from -Z) and a reversed copy for the back.
            var verts = new Vector3[]
            {
                new(-w * 0.5f, 0f, 0f),            // 0 TL
                new( w * 0.5f, 0f, 0f),            // 1 TR
                new( w * 0.5f, -h, 0f),            // 2 BR
                new( 0f, -h + notch, 0f),          // 3 notch apex
                new(-w * 0.5f, -h, 0f),            // 4 BL
                // back copy
                new(-w * 0.5f, 0f, 0f),
                new( w * 0.5f, 0f, 0f),
                new( w * 0.5f, -h, 0f),
                new( 0f, -h + notch, 0f),
                new(-w * 0.5f, -h, 0f),
            };
            var uvs = new Vector2[verts.Length];
            for (int i = 0; i < verts.Length; i++)
                uvs[i] = new Vector2((verts[i].x + w * 0.5f) / w, (verts[i].y + h) / h);
            var normals = new Vector3[verts.Length];
            for (int i = 0; i < 5; i++) { normals[i] = Vector3.back; normals[i + 5] = Vector3.forward; }

            var tris = new int[]
            {
                // front (viewed from -Z)
                0, 1, 3,   1, 2, 3,   0, 3, 4,
                // back (reversed winding on the duplicate set)
                5, 8, 6,   6, 8, 7,   5, 9, 8,
            };

            var mesh = new Mesh { name = "BannerCloth_SwallowTail" };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = tris;
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            _clothMaterial = MakeMaterial(Color.white, metallic: 0f, smoothness: 0.25f);
            _clothMaterial.name = "BannerCloth_Runtime";
            renderer.sharedMaterial = _clothMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        private void BuildTexts(float topY)
        {
            // Three lines over the cloth, front side. The texture is image
            // only; texts travel as strings so they stay crisp at any size.
            _textTop = MakeText("Banner_TextTop", new Vector3(0f, topY - clothHeight * 0.14f, -0.012f), 0.60f);
            _textMiddle = MakeText("Banner_TextMiddle", new Vector3(0f, topY - clothHeight * 0.50f, -0.012f), 0.75f);
            _textBottom = MakeText("Banner_TextBottom", new Vector3(0f, topY - clothHeight * 0.72f, -0.012f), 0.60f);
        }

        private TextMesh MakeText(string name, Vector3 localPos, float scale01)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            var tm = go.AddComponent<TextMesh>();
            tm.text = "";
            tm.fontSize = 48;
            tm.characterSize = 0.012f * scale01 * (clothWidth / 0.84f);
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold;
            tm.color = new Color(0.14f, 0.10f, 0.08f);
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            return tm;
        }

        private GameObject AddPart(PrimitiveType type, Material mat, Vector3 pos, Vector3 euler, Vector3 scale, string name)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            // The part is decoration; the owner (block/display) supplies the
            // collider. Destroy, not DestroyImmediate - builds can run inside
            // physics callbacks.
            var col = go.GetComponent<Collider>();
            if (col != null) { col.enabled = false; Destroy(col); }
            return go;
        }

        private static Material MakeMaterial(Color color, float metallic, float smoothness)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            return mat;
        }
    }
}
