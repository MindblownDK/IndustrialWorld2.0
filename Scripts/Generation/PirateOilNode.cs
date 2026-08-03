// Assets/Scripts/VoxelEngine/Generation/PirateOilNode.cs
// Runtime marker for one rare, infinite Pirate World oil site. The terrain
// puddle/bore/reservoir remains the visual geology; this marker preserves the
// node's infinite extraction identity even if a player drains visible oil voxels.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Combat;
using VoxelEngine.Core;
using VoxelEngine.Cosmos;
using VoxelEngine.Items;

namespace VoxelEngine.Generation
{
    [DisallowMultipleComponent]
    public sealed class PirateOilNode : MonoBehaviour
    {
        private static readonly List<PirateOilNode> s_nodes = new(32);

        public SphereWorld World { get; private set; }
        public Vector3Int SurfaceVoxel { get; private set; }
        public Vector3Int ReservoirVoxel { get; private set; }
        public float PumpRadius { get; private set; } = 4.5f;

        private void OnEnable()
        {
            if (!s_nodes.Contains(this)) s_nodes.Add(this);
        }

        private void OnDisable()
        {
            s_nodes.Remove(this);
        }

        public static PirateOilNode Ensure(SphereWorld world, Vector3Int surfaceVoxel, Vector3Int reservoirVoxel)
        {
            if (world == null || world.body == null || world.body.settings == null
                || !world.body.settings.CanGenerateInfiniteJackPumpNodes) return null;
            for (int i = s_nodes.Count - 1; i >= 0; i--)
            {
                var node = s_nodes[i];
                if (node == null) { s_nodes.RemoveAt(i); continue; }
                if (node.World == world && (node.ReservoirVoxel - reservoirVoxel).sqrMagnitude <= 4)
                    return node;
            }

            var go = new GameObject("Infinite Pirate Oil Node");
            go.transform.SetParent(world.body.transform, false);
            go.transform.position = world.body.transform.TransformPoint(
                ((Vector3)surfaceVoxel + Vector3.one * 0.5f) * VoxelConstants.VOXEL_SIZE);
            var created = go.AddComponent<PirateOilNode>();
            created.World = world;
            created.SurfaceVoxel = surfaceVoxel;
            created.ReservoirVoxel = reservoirVoxel;
            created.PumpRadius = 4.5f;
            created.SpawnRuinedPumpVisual();
            return created;
        }

        private void SpawnRuinedPumpVisual()
        {
            var ruined = new GameObject("Ruined Jack Pump");
            ruined.transform.SetParent(transform, false);
            ruined.transform.localPosition = Vector3.zero;

            // Add collider so player can aim at and break it
            var col = ruined.AddComponent<BoxCollider>();
            col.size = new Vector3(3f, 3.5f, 3f);
            col.center = new Vector3(0f, 1.75f, 0f);

            var brokenPump = ruined.AddComponent<BrokenJackPump>();
            brokenPump.node = this;

            // Industrial rust/weathered primitives
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = new Color(0.38f, 0.22f, 0.15f);

            var baseBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseBox.name = "BaseFrame";
            baseBox.transform.SetParent(ruined.transform, false);
            baseBox.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            baseBox.transform.localScale = new Vector3(2.4f, 1.2f, 2.4f);
            Object.Destroy(baseBox.GetComponent<Collider>());
            if (baseBox.TryGetComponent<Renderer>(out var r1)) r1.sharedMaterial = mat;

            var tower = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tower.name = "Tower";
            tower.transform.SetParent(ruined.transform, false);
            tower.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            tower.transform.localScale = new Vector3(0.8f, 2.4f, 0.8f);
            tower.transform.localRotation = Quaternion.Euler(0f, 0f, 15f);
            Object.Destroy(tower.GetComponent<Collider>());
            if (tower.TryGetComponent<Renderer>(out var r2)) r2.sharedMaterial = mat;
        }

        public static bool IsPumpableNear(SphereWorld world, Vector3 pumpWorldPosition, float extraReach = 0.75f)
        {
            if (world == null || world.body == null || world.body.settings == null
                || !world.body.settings.CanGenerateInfiniteJackPumpNodes) return false;
            for (int i = s_nodes.Count - 1; i >= 0; i--)
            {
                var node = s_nodes[i];
                if (node == null) { s_nodes.RemoveAt(i); continue; }
                if (node.World != world) continue;
                float reach = node.PumpRadius + Mathf.Max(0f, extraReach);
                if ((node.transform.position - pumpWorldPosition).sqrMagnitude <= reach * reach)
                    return true;
            }
            return false;
        }
    }

    [DisallowMultipleComponent]
    public sealed class BrokenJackPump : MonoBehaviour, IDamageable
    {
        public PirateOilNode node;
        private int _hp = 120;

        public bool IsAlive => _hp > 0;

        public void TakeDamage(DamageEvent e)
        {
            if (!IsAlive) return;
            _hp -= Mathf.Max(1, Mathf.RoundToInt(e.amount));
            if (_hp <= 0)
            {
                var inv = Object.FindAnyObjectByType<Inventory>();
                if (inv != null)
                {
                    ItemDefinition ironPlate = null;
                    foreach (var it in Resources.LoadAll<ItemDefinition>(""))
                    {
                        if (it != null && (it.name == "Item_IronPlate" || it.displayName == "Iron Plate"))
                        {
                            ironPlate = it;
                            break;
                        }
                    }
                    if (ironPlate != null)
                    {
                        inv.Add(ironPlate, 4);
                        VoxelEngine.UI.BuildFeedbackHud.Show("Salvage", "Ruined Jack Pump: +4 Iron Plates", null, new Color(0.78f, 0.80f, 0.85f));
                    }
                }
                Destroy(gameObject);
            }
        }
    }
}
