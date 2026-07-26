// Assets/Scripts/VoxelEngine/Simulation/HighVoltagePole.cs
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Simulation
{
    public class HighVoltagePole : VoltageStationBase, IVoltageStation
    {
        [Header("Tower Configuration")]
        public float towerHeight = 12f;
        public float baseWidth = 3f;
        public float topWidth = 1.2f;
        public int crossArmLevels = 2;
        public float crossArmLength = 3.5f;

        // IVoltageStation implementation
        public override float TotalProduced => network != null ? network.producedThisTick : 0f;
        public override float TotalConsumed => network != null ? network.consumedThisTick : 0f;
        public override float MaxCapacity => float.PositiveInfinity;

        private PowerNetwork network => _powerNode != null ? _powerNode.network : null;
        private PowerNode _powerNode;

        protected override void Awake()
        {
            base.Awake();
            _powerNode = GetComponent<PowerNode>();
            if (_powerNode == null) _powerNode = gameObject.AddComponent<PowerCable>();
            
            isHighVoltage = true;
            connectionPointOffset = new Vector3(0, towerHeight - 1f, 0);
            wireReach = 200f;

            BuildTowerVisuals();
        }

        // ── Tower Visuals ─────────────────────────────────────────────

        private static Material _steelMat;
        private static Material _insulatorMat;

        private void BuildTowerVisuals()
        {
            EnsureMaterials();
            float h = towerHeight;
            float bw = baseWidth * 0.5f;
            float tw = topWidth * 0.5f;

            Vector3[] baseCorners = new[]
            {
                new Vector3(-bw, 0, -bw),
                new Vector3( bw, 0, -bw),
                new Vector3( bw, 0,  bw),
                new Vector3(-bw, 0,  bw)
            };
            Vector3[] topCorners = new[]
            {
                new Vector3(-tw, h, -tw),
                new Vector3( tw, h, -tw),
                new Vector3( tw, h,  tw),
                new Vector3(-tw, h,  tw)
            };

            for (int i = 0; i < 4; i++)
                CreateBeam(baseCorners[i], topCorners[i], 0.10f);

            int braceCount = Mathf.Max(2, (int)(h / 3f));
            for (int b = 0; b < braceCount; b++)
            {
                float t0 = (float)b / braceCount;
                float t1 = (float)(b + 1) / braceCount;

                for (int face = 0; face < 4; face++)
                {
                    int next = (face + 1) % 4;
                    Vector3 bl = Vector3.Lerp(baseCorners[face], topCorners[face], t0);
                    Vector3 br = Vector3.Lerp(baseCorners[next], topCorners[next], t0);
                    Vector3 tl = Vector3.Lerp(baseCorners[face], topCorners[face], t1);
                    Vector3 tr = Vector3.Lerp(baseCorners[next], topCorners[next], t1);

                    CreateBeam(bl, tr, 0.04f);
                    CreateBeam(br, tl, 0.04f);
                    CreateBeam(tl, tr, 0.05f);
                }
            }

            for (int arm = 0; arm < crossArmLevels; arm++)
            {
                float armY = h - 0.5f - arm * 1.8f;
                CreateBeam(new Vector3(-crossArmLength, armY, 0), new Vector3( crossArmLength, armY, 0), 0.08f);
                float towerWidthAtArm = Mathf.Lerp(bw, tw, armY / h);
                CreateBeam(new Vector3(-crossArmLength, armY, 0), new Vector3(-towerWidthAtArm, armY, 0), 0.06f);
                CreateBeam(new Vector3( crossArmLength, armY, 0), new Vector3( towerWidthAtArm, armY, 0), 0.06f);
                CreateBeam(new Vector3(-crossArmLength, armY, 0), new Vector3(-towerWidthAtArm, armY - 1.0f, 0), 0.04f);
                CreateBeam(new Vector3( crossArmLength, armY, 0), new Vector3( towerWidthAtArm, armY - 1.0f, 0), 0.04f);

                float[] insulatorX = { -crossArmLength + 0.3f, 0f, crossArmLength - 0.3f };
                foreach (var ix in insulatorX)
                    CreateInsulatorString(new Vector3(ix, armY, 0), 0.8f);
            }

            CreateBeam(new Vector3(0, h, 0), new Vector3(0, h + 1.5f, 0), 0.04f);

            var peak = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            peak.name = "LightningRod";
            peak.transform.SetParent(transform, false);
            peak.transform.localPosition = new Vector3(0, h + 1.5f, 0);
            peak.transform.localScale = Vector3.one * 0.12f;
            if (peak.TryGetComponent<Collider>(out var col)) Destroy(col);
            peak.GetComponent<MeshRenderer>().material = _steelMat;
        }

        private void CreateBeam(Vector3 localA, Vector3 localB, float thickness)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Beam";
            go.transform.SetParent(transform, false);
            Vector3 mid = (localA + localB) * 0.5f;
            float length = Vector3.Distance(localA, localB);
            Vector3 dir = (localB - localA).normalized;
            go.transform.localPosition = mid;
            go.transform.localScale = new Vector3(thickness, thickness, length);
            go.transform.localRotation = Quaternion.LookRotation(dir);
            if (go.TryGetComponent<Collider>(out var col)) Destroy(col);
            go.GetComponent<MeshRenderer>().material = _steelMat;
        }

        private void CreateInsulatorString(Vector3 localTop, float length)
        {
            int discs = Mathf.Max(2, (int)(length / 0.15f));
            for (int d = 0; d < discs; d++)
            {
                var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                disc.name = "Insulator";
                disc.transform.SetParent(transform, false);
                disc.transform.localPosition = localTop + Vector3.down * (d * 0.15f + 0.1f);
                disc.transform.localScale = new Vector3(0.12f, 0.03f, 0.12f);
                if (disc.TryGetComponent<Collider>(out var col)) Destroy(col);
                disc.GetComponent<MeshRenderer>().material = _insulatorMat;
            }
        }

        private static void EnsureMaterials()
        {
            if (_steelMat != null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _steelMat = new Material(shader) { color = new Color(0.52f, 0.54f, 0.56f) };
            _steelMat.SetFloat("_Metallic", 0.8f);
            _steelMat.SetFloat("_Smoothness", 0.45f);
            _insulatorMat = new Material(shader) { color = new Color(0.65f, 0.55f, 0.40f) };
            _insulatorMat.SetFloat("_Metallic", 0.05f);
            _insulatorMat.SetFloat("_Smoothness", 0.7f);
        }
    }
}
