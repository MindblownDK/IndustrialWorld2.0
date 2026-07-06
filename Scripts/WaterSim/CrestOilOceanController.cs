using System.Reflection;
using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Items;

namespace VoxelEngine.WaterSim
{
    /// <summary>
    /// Crest Oil Surface – v3.20.0
    /// Secondary Crest OceanRenderer instance tuned for crude oil visuals.
    /// Activates only over oil reservoirs / puddles.
    /// </summary>
    [DefaultExecutionOrder(-45)]
    public class CrestOilOceanController : MonoBehaviour
    {
        public Transform viewpoint;
        [Range(32f, 512f)] public float oilSearchRadius = 256f;
        public float heightOffset = 0.04f;
        public Material oilMaterialOverride;

        private Component _oceanRenderer;
        private Behaviour _behaviour;

        private float _nextScan;
        private bool _oilFound;
        private Vector3 _oilPos;

        private void OnEnable()
        {
            CacheOcean();
            ApplyOilMaterial();
            // v3.23.0 – Voxel water/oil surfaces are authoritative visuals.
            // Do NOT toggle RenderingEnabled here; that hides ALL water.
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime < _nextScan) { return; }
            _nextScan = Time.unscaledTime + 0.4f;

            var view = viewpoint != null ? viewpoint : Camera.main != null ? Camera.main.transform : null;
            if (view == null) return;
            var world = ActiveWorld.Current;
            if (world == null) return;

            _oilFound = FindOilNear(view.position, oilSearchRadius, out _oilPos);
            SetActive(_oilFound);

            if (_oilFound)
            {
                Vector3 up = PlanetWaterUtility.IsPlanetWorld ? PlanetWaterUtility.WorldUp(_oilPos) : Vector3.up;
                transform.SetPositionAndRotation(_oilPos + up * heightOffset, Quaternion.FromToRotation(Vector3.up, up));
            }
        }

        private bool FindOilNear(Vector3 center, float radius, out Vector3 oilPos)
        {
            oilPos = center;
            var world = ActiveWorld.Current;
            if (world == null) return false;

            int step = 8;
            int r = Mathf.CeilToInt(radius / VoxelConstants.VOXEL_SIZE);
            Vector3Int c = world.WorldToVoxel(center);
            for (int y = -6; y <= 6; y += 2)
            for (int z = -r; z <= r; z += step)
            for (int x = -r; x <= r; x += step)
            {
                var p = c + new Vector3Int(x, y, z);
                var v = world.GetVoxelWorld(p);
                if (FluidMaterialUtility.IsFluid(v) && FluidMaterialUtility.LiquidFromVoxel(v) == LiquidType.CrudeOil)
                {
                    oilPos = ((Vector3)p + Vector3.one * 0.5f) * VoxelConstants.VOXEL_SIZE;
                    return true;
                }
            }
            return false;
        }

        private void CacheOcean()
        {
            if (_oceanRenderer != null) return;
            var t = System.Type.GetType("Crest.OceanRenderer, Crest");
            if (t != null) _oceanRenderer = GetComponent(t);
            if (_oceanRenderer != null) _behaviour = _oceanRenderer as Behaviour;
        }

        private void ApplyOilMaterial()
        {
            if (oilMaterialOverride == null || _oceanRenderer == null) return;
            var tp = _oceanRenderer.GetType();
            var prop = tp.GetProperty("OceanMaterial", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null && prop.CanWrite) prop.SetValue(_oceanRenderer, oilMaterialOverride);
            var field = tp.GetField("_material", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null) field.SetValue(_oceanRenderer, oilMaterialOverride);
        }

        private void SetActive(bool active)
        {
            if (_behaviour != null && _behaviour.enabled != active) _behaviour.enabled = active;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                r.enabled = active;
        }
    }
}
