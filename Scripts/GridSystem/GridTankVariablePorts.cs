// Assets/Scripts/VoxelEngine/GridSystem/GridTankVariablePorts.cs
//
// Additive, save-compatible variable pipe ports for grid gas/liquid tanks.
// Works like maritime engine variable ports: the player aims at a tank face while
// holding the matching pipe, a small colored connector is installed on the tank
// hull, and the pipe snaps to the Detail lattice cell just outside that point.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Maritime;

namespace VoxelEngine.GridSystem
{
    public enum GridTankPortFamily : byte
    {
        Liquid = 0,
        Gas = 1,
    }

    [System.Serializable]
    public class GridTankPortRecord
    {
        public int family;
        public Vector3 localPos;
        public Vector3 localOutward;

        public GridTankPortRecord() { }
        public GridTankPortRecord(GridTankPortFamily family, Vector3 localPos, Vector3 localOutward)
        {
            this.family = (int)family;
            this.localPos = localPos;
            this.localOutward = localOutward;
        }

        public GridTankPortFamily Family => (GridTankPortFamily)family;
    }

    [DisallowMultipleComponent]
    public sealed class GridTankVariablePorts : MonoBehaviour
    {
        [SerializeField] private List<GridTankPortRecord> _records = new();
        private readonly List<Transform> _runtimePorts = new(4);

        public bool HasRecords => _records != null && _records.Count > 0;

        public static string PrefixFor(GridTankPortFamily family)
            => family == GridTankPortFamily.Gas ? "Port_GasIO" : "Port_LiquidIO";

        public static Color ColorFor(GridTankPortFamily family)
            => family == GridTankPortFamily.Gas
                ? new Color(0.45f, 0.75f, 1.00f, 1f)
                : new Color(0.20f, 0.55f, 1.00f, 1f);

        public static string LabelFor(GridTankPortFamily family)
            => family == GridTankPortFamily.Gas ? "Gas tank port" : "Liquid tank port";

        public Transform AddPort(GridTankPortFamily family, Vector3 localPos, Vector3 localOutward)
        {
            if (localOutward.sqrMagnitude < 0.0001f) localOutward = Vector3.up;
            localOutward = localOutward.normalized;

            var existing = FindNear(family, localPos, 0.08f);
            if (existing != null) return existing;

            var record = new GridTankPortRecord(family, localPos, localOutward);
            _records.Add(record);
            var port = BuildPortObject(record);
            if (port != null) _runtimePorts.Add(port);
            return port;
        }

        public List<GridTankPortRecord> CaptureRecords()
        {
            var copy = new List<GridTankPortRecord>(_records.Count);
            for (int i = 0; i < _records.Count; i++)
            {
                var r = _records[i];
                if (r == null) continue;
                copy.Add(new GridTankPortRecord(r.Family, r.localPos, r.localOutward));
            }
            return copy;
        }

        public void RebuildFromRecords(List<GridTankPortRecord> records)
        {
            ClearDynamicObjects();
            _records.Clear();
            if (records == null) return;
            for (int i = 0; i < records.Count; i++)
            {
                var r = records[i];
                if (r == null) continue;
                if (r.localOutward.sqrMagnitude < 0.0001f) r.localOutward = Vector3.up;
                _records.Add(r);
                var port = BuildPortObject(r);
                if (port != null) _runtimePorts.Add(port);
            }
        }

        private Transform FindNear(GridTankPortFamily family, Vector3 localPos, float radius)
        {
            string prefix = PrefixFor(family);
            float r2 = radius * radius;
            for (int i = 0; i < _runtimePorts.Count; i++)
            {
                var t = _runtimePorts[i];
                if (t == null || !t.name.StartsWith(prefix, System.StringComparison.Ordinal)) continue;
                if ((t.localPosition - localPos).sqrMagnitude <= r2) return t;
            }
            return null;
        }

        private void ClearDynamicObjects()
        {
            for (int i = 0; i < _runtimePorts.Count; i++)
                if (_runtimePorts[i] != null) Destroy(_runtimePorts[i].gameObject);
            _runtimePorts.Clear();
        }

        private Transform BuildPortObject(GridTankPortRecord r)
        {
            var family = r.Family;
            var container = new GameObject(PrefixFor(family) + "_V");
            container.transform.SetParent(transform, false);
            container.transform.localPosition = r.localPos;

            Vector3 dir = r.localOutward.sqrMagnitude > 0.0001f ? r.localOutward.normalized : Vector3.up;
            Vector3 guide = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            container.transform.localRotation = Quaternion.LookRotation(dir, guide);

            var facing = container.AddComponent<MaritimePortFacing>();
            facing.localOutward = dir;

            Color col = ColorFor(family);
            var ringMat = PortMaterial(col, col * 0.45f);
            var eyeMat = PortMaterial(col, col * 0.95f);

            var collar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            collar.name = "Collar";
            collar.transform.SetParent(container.transform, false);
            collar.transform.localPosition = new Vector3(0f, 0f, 0.005f);
            collar.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            collar.transform.localScale = new Vector3(0.12f, 0.02f, 0.12f);
            ApplyVisual(collar, ringMat);

            var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eye.name = "Eye";
            eye.transform.SetParent(container.transform, false);
            eye.transform.localPosition = new Vector3(0f, 0f, 0.018f);
            eye.transform.localScale = new Vector3(0.08f, 0.08f, 0.045f);
            ApplyVisual(eye, eyeMat);

            return container.transform;
        }

        private static void ApplyVisual(GameObject go, Material mat)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = mat;
        }

        private static readonly Dictionary<int, Material> s_matCache = new();
        private static Material PortMaterial(Color color, Color emissive)
        {
            int key = (Mathf.RoundToInt(color.r * 255) << 16)
                | (Mathf.RoundToInt(color.g * 255) << 8)
                | Mathf.RoundToInt(color.b * 255);
            if (s_matCache.TryGetValue(key, out var cached) && cached != null) return cached;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.35f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.70f);
            if (mat.HasProperty("_EmissionColor")) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", emissive); }
            s_matCache[key] = mat;
            return mat;
        }
    }
}
