// Assets/Scripts/VoxelEngine/Generation/DeepOreField.cs
//
// DEEP ORE NODES — large, finite deposits that justify an outpost.
//
// The roadmap asks for "large, finite ore nodes" that "encourage outpost building".
// Those two clauses are the same requirement stated twice: a node only encourages an
// outpost if it is worth travelling to AND worth staying at, which means it must be
// rich enough to matter and rare enough that you cannot simply find another.
//
// WHY THIS IS NOT JUST MORE ORE VOXELS
// Hand-mining an ore vein is already the early game. A deep node is deliberately a
// DIFFERENT verb: it cannot be mined by hand at all. It sits below the voxel world,
// is found with a survey tool, and is extracted by a powered machine that runs
// unattended. That turns "I found ore" into "I am going to build here", which is the
// behaviour the roadmap is actually asking for.
//
// WHERE A NODE EXISTS — derived, exactly like HazardField
// Node PLACEMENT is a pure function of (body seed, position), sampled from Worley
// noise. No spawning, no registry, no save data for where nodes are. That means every
// existing world already has them, and two players on the same seed find the same
// nodes.
//
// WHAT A NODE HAS LEFT — stored, because it must be
// Depletion is the one thing that CANNOT be derived: it is a record of what the player
// did. So the field stores a small dictionary of "node key -> amount taken", keyed by
// the node's integer cell coordinate. An untouched node has no entry, so a fresh world
// and a legacy save both cost zero bytes. This is the minimum possible save surface
// for a finite resource.

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Materials;

namespace VoxelEngine.Generation
{
    /// <summary>A deep deposit: what it is, where it is, and how much is left.</summary>
    public readonly struct DeepOreNode
    {
        /// <summary>Integer cell coordinate. Stable identity for save/lookup.</summary>
        public readonly int2 Cell;

        /// <summary>Centre of the node in body-local world space.</summary>
        public readonly Vector3 Centre;

        public readonly MaterialId Material;

        /// <summary>Total the node ever held, in items.</summary>
        public readonly int Capacity;

        /// <summary>How much has already been taken.</summary>
        public readonly int Extracted;

        /// <summary>Radius within which an extractor can tap this node, in metres.</summary>
        public readonly float Radius;

        public DeepOreNode(int2 cell, Vector3 centre, MaterialId material,
            int capacity, int extracted, float radius)
        {
            Cell = cell; Centre = centre; Material = material;
            Capacity = capacity; Extracted = extracted; Radius = radius;
        }

        public int Remaining => Mathf.Max(0, Capacity - Extracted);
        public bool IsDepleted => Remaining <= 0;
        public float Fraction01 => Capacity > 0 ? Mathf.Clamp01(Remaining / (float)Capacity) : 0f;
        public bool IsValid => Capacity > 0;
    }

    public static class DeepOreField
    {
        // ════════════════════════════════════════════════════════════════
        //  TUNING
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Spacing of the node lattice in metres. Deliberately large: a node you can walk
        /// between in a minute does not justify an outpost, it justifies a longer walk.
        /// </summary>
        private const float NODE_CELL_SIZE = 900f;

        /// <summary>
        /// Fraction of cells that actually contain a node. Well under half, so a survey is
        /// a real search rather than a formality.
        /// </summary>
        private const float NODE_RARITY = 0.30f;

        /// <summary>How close an extractor must be to the node centre to tap it.</summary>
        public const float NODE_RADIUS = 26f;

        /// <summary>Range of a node's total yield, in items.</summary>
        private const int MIN_CAPACITY = 9000;
        private const int MAX_CAPACITY = 34000;

        /// <summary>
        /// Which ores appear as deep nodes. Deliberately the industrially useful ones: a
        /// deep node should relieve a bottleneck, not hand out stone.
        /// </summary>
        private static readonly MaterialId[] NodeMaterials =
        {
            MaterialId.Iron, MaterialId.Copper, MaterialId.Coal, MaterialId.Nickel,
            MaterialId.Silicon, MaterialId.Cobalt, MaterialId.Lithium,
            MaterialId.Uranium, MaterialId.Platinum, MaterialId.Gold,
        };

        /// <summary>
        /// Rarer materials appear less often. Index-matched to NodeMaterials; higher weight
        /// means more common. Without this, uranium would be as common as iron and the
        /// whole progression ladder would flatten.
        /// </summary>
        private static readonly float[] MaterialWeights =
        {
            10f, 9f, 8f, 6f,
            6f, 4f, 3.5f,
            1.5f, 1.2f, 1f,
        };

        // ════════════════════════════════════════════════════════════════
        //  DEPLETION STORE  (the only saved state)
        // ════════════════════════════════════════════════════════════════

        /// <summary>Key is "bodyName|cellX|cellY". Absent entry = untouched node.</summary>
        private static readonly Dictionary<string, int> _extracted = new(64);

        private static string KeyOf(string bodyName, int2 cell)
            => $"{bodyName}|{cell.x}|{cell.y}";

        /// <summary>Everything taken so far, for the save layer. Empty on a fresh world.</summary>
        public static IReadOnlyDictionary<string, int> ExtractionState => _extracted;

        public static void LoadExtractionState(IEnumerable<KeyValuePair<string, int>> entries)
        {
            _extracted.Clear();
            if (entries == null) return;
            foreach (var kv in entries)
            {
                if (string.IsNullOrEmpty(kv.Key) || kv.Value <= 0) continue;
                _extracted[kv.Key] = kv.Value;
            }
        }

        public static void ClearExtractionState() => _extracted.Clear();

        // ════════════════════════════════════════════════════════════════
        //  QUERY
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// The node whose radius contains <paramref name="worldPosition"/>, if any.
        /// Checks the 3x3 cell neighbourhood because a node sits at a jittered point
        /// inside its cell and its radius can cross a cell boundary.
        /// </summary>
        public static bool TryGetNodeAt(Vector3 worldPosition, out DeepOreNode node)
        {
            node = default;
            var body = GravityProvider.ActiveBody;
            if (body == null || body.settings == null) return false;

            int seed = body.genParams.seed;
            string bodyName = body.settings.bodyName;

            int cx = Mathf.FloorToInt(worldPosition.x / NODE_CELL_SIZE);
            int cz = Mathf.FloorToInt(worldPosition.z / NODE_CELL_SIZE);

            float bestSq = float.MaxValue;
            bool found = false;

            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var cell = new int2(cx + dx, cz + dz);
                if (!TryBuildNode(cell, seed, bodyName, out var candidate)) continue;

                float dSq = (candidate.Centre - worldPosition).sqrMagnitude;
                if (dSq > candidate.Radius * candidate.Radius) continue;
                if (dSq >= bestSq) continue;

                bestSq = dSq;
                node = candidate;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// The nearest node within <paramref name="searchRadius"/>, whether or not the
        /// position is inside it. This is what the survey tool reports, so it can point
        /// the player at a deposit they have not reached yet.
        /// </summary>
        public static bool TryFindNearest(Vector3 worldPosition, float searchRadius,
            out DeepOreNode node, out float distance)
        {
            node = default;
            distance = 0f;

            var body = GravityProvider.ActiveBody;
            if (body == null || body.settings == null) return false;

            int seed = body.genParams.seed;
            string bodyName = body.settings.bodyName;

            int span = Mathf.Max(1, Mathf.CeilToInt(searchRadius / NODE_CELL_SIZE) + 1);
            int cx = Mathf.FloorToInt(worldPosition.x / NODE_CELL_SIZE);
            int cz = Mathf.FloorToInt(worldPosition.z / NODE_CELL_SIZE);

            float bestSq = searchRadius * searchRadius;
            bool found = false;

            for (int dz = -span; dz <= span; dz++)
            for (int dx = -span; dx <= span; dx++)
            {
                var cell = new int2(cx + dx, cz + dz);
                if (!TryBuildNode(cell, seed, bodyName, out var candidate)) continue;

                float dSq = (candidate.Centre - worldPosition).sqrMagnitude;
                if (dSq >= bestSq) continue;

                bestSq = dSq;
                node = candidate;
                found = true;
            }

            if (found) distance = Mathf.Sqrt(bestSq);
            return found;
        }

        /// <summary>
        /// Every node within <paramref name="searchRadius"/>, nearest first. This is the
        /// orbital survey query: a satellite sees the whole field at once rather than the
        /// single nearest deposit a hand scanner reports.
        ///
        /// Capped by <paramref name="maxResults"/> because a wide sweep over a large
        /// radius can cover hundreds of cells, and a readout nobody can scroll is not
        /// more useful than a short ranked one.
        /// </summary>
        public static int SurveyArea(Vector3 worldPosition, float searchRadius,
            List<DeepOreNode> results, int maxResults = 24)
        {
            results.Clear();

            var body = GravityProvider.ActiveBody;
            if (body == null || body.settings == null) return 0;

            int seed = body.genParams.seed;
            string bodyName = body.settings.bodyName;

            int span = Mathf.Max(1, Mathf.CeilToInt(searchRadius / NODE_CELL_SIZE) + 1);
            int cx = Mathf.FloorToInt(worldPosition.x / NODE_CELL_SIZE);
            int cz = Mathf.FloorToInt(worldPosition.z / NODE_CELL_SIZE);

            float rSq = searchRadius * searchRadius;

            for (int dz = -span; dz <= span; dz++)
            for (int dx = -span; dx <= span; dx++)
            {
                var cell = new int2(cx + dx, cz + dz);
                if (!TryBuildNode(cell, seed, bodyName, out var node)) continue;
                if ((node.Centre - worldPosition).sqrMagnitude > rSq) continue;
                results.Add(node);
            }

            results.Sort((a, b) =>
                (a.Centre - worldPosition).sqrMagnitude
                .CompareTo((b.Centre - worldPosition).sqrMagnitude));

            if (results.Count > maxResults) results.RemoveRange(maxResults, results.Count - maxResults);
            return results.Count;
        }

        /// <summary>
        /// Takes up to <paramref name="amount"/> from a node and records the depletion.
        /// Returns what was actually removed, which is less than asked near exhaustion.
        /// </summary>
        public static int Extract(DeepOreNode node, int amount)
        {
            if (amount <= 0 || !node.IsValid) return 0;

            var body = GravityProvider.ActiveBody;
            if (body == null || body.settings == null) return 0;

            string key = KeyOf(body.settings.bodyName, node.Cell);
            _extracted.TryGetValue(key, out int already);

            int remaining = Mathf.Max(0, node.Capacity - already);
            int taken = Mathf.Min(amount, remaining);
            if (taken <= 0) return 0;

            _extracted[key] = already + taken;
            return taken;
        }

        /// <summary>
        /// Puts ore back that an extractor took but could not store. Without this, a full
        /// output buffer would quietly destroy the deposit one item at a time, which is
        /// the worst possible failure for a finite resource because it is invisible.
        /// </summary>
        public static void Refund(DeepOreNode node, int amount)
        {
            if (amount <= 0 || !node.IsValid) return;

            var body = GravityProvider.ActiveBody;
            if (body == null || body.settings == null) return;

            string key = KeyOf(body.settings.bodyName, node.Cell);
            if (!_extracted.TryGetValue(key, out int already)) return;

            int restored = Mathf.Max(0, already - amount);
            if (restored <= 0) _extracted.Remove(key);
            else _extracted[key] = restored;
        }

        /// <summary>Re-reads a node so a caller holding a stale copy sees current depletion.</summary>
        public static bool TryRefresh(DeepOreNode stale, out DeepOreNode fresh)
        {
            fresh = default;
            var body = GravityProvider.ActiveBody;
            if (body == null || body.settings == null) return false;
            return TryBuildNode(stale.Cell, body.genParams.seed, body.settings.bodyName, out fresh);
        }

        // ════════════════════════════════════════════════════════════════
        //  DERIVATION
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds the node for a cell, or reports that the cell is barren. Entirely
        /// deterministic from (cell, seed), so it never needs to be stored or streamed.
        /// </summary>
        private static bool TryBuildNode(int2 cell, int seed, string bodyName, out DeepOreNode node)
        {
            node = default;

            uint h = Hash((uint)cell.x, (uint)cell.y, (uint)seed);

            // Rarity gate first: most cells hold nothing.
            if (Unit(h) > NODE_RARITY) return false;

            // Jitter the centre inside the cell so nodes do not sit on a visible lattice.
            h = Scramble(h);
            float jx = Unit(h);
            h = Scramble(h);
            float jz = Unit(h);

            float centreX = (cell.x + 0.15f + jx * 0.7f) * NODE_CELL_SIZE;
            float centreZ = (cell.y + 0.15f + jz * 0.7f) * NODE_CELL_SIZE;

            h = Scramble(h);
            MaterialId material = PickMaterial(Unit(h));

            h = Scramble(h);
            int capacity = Mathf.RoundToInt(Mathf.Lerp(MIN_CAPACITY, MAX_CAPACITY, Unit(h)));

            // Rarer ores come in smaller nodes, so a uranium find is valuable without
            // being a permanent solution to uranium.
            float scarcity = ScarcityScale(material);
            capacity = Mathf.Max(600, Mathf.RoundToInt(capacity * scarcity));

            string key = KeyOf(bodyName, cell);
            _extracted.TryGetValue(key, out int already);

            // The vertical position is the surface: an extractor is placed on the ground
            // above the deposit, not lowered into it. Y is ignored by every query, which
            // keeps the node valid regardless of local terrain height.
            var centre = new Vector3(centreX, 0f, centreZ);

            node = new DeepOreNode(cell, centre, material, capacity, already, NODE_RADIUS);
            return true;
        }

        private static MaterialId PickMaterial(float roll)
        {
            float total = 0f;
            for (int i = 0; i < MaterialWeights.Length; i++) total += MaterialWeights[i];

            float pick = roll * total;
            for (int i = 0; i < NodeMaterials.Length; i++)
            {
                pick -= MaterialWeights[i];
                if (pick <= 0f) return NodeMaterials[i];
            }
            return NodeMaterials[0];
        }

        private static float ScarcityScale(MaterialId material) => material switch
        {
            MaterialId.Uranium => 0.30f,
            MaterialId.Platinum => 0.35f,
            MaterialId.Gold => 0.40f,
            MaterialId.Lithium => 0.60f,
            MaterialId.Cobalt => 0.70f,
            _ => 1f,
        };

        // Deterministic integer hashing. Chosen over System.Random so a node is a pure
        // function of its coordinates with no sequence state to get out of step.
        private static uint Hash(uint x, uint y, uint seed)
        {
            uint h = x * 73856093u ^ y * 19349663u ^ seed * 83492791u;
            return Scramble(h);
        }

        private static uint Scramble(uint h)
        {
            h ^= h >> 16;
            h *= 0x7feb352du;
            h ^= h >> 15;
            h *= 0x846ca68bu;
            h ^= h >> 16;
            return h;
        }

        /// <summary>Hash to a 0..1 float.</summary>
        private static float Unit(uint h) => (h & 0x00FFFFFFu) / (float)0x01000000u;

        /// <summary>
        /// Item id of the survey scanner. Owned here rather than in the HUD because both
        /// the UI and the editor setup step need it, and an id that lives in a UI class is
        /// a coupling waiting to be broken by a refactor.
        /// </summary>
        public const string ScannerItemId = "deepsurveyscanner";

        /// <summary>Player-facing name of a node material.</summary>
        public static string MaterialName(MaterialId material) => material switch
        {
            MaterialId.Iron => "Iron",
            MaterialId.Copper => "Copper",
            MaterialId.Coal => "Coal",
            MaterialId.Nickel => "Nickel",
            MaterialId.Silicon => "Silicon",
            MaterialId.Cobalt => "Cobalt",
            MaterialId.Lithium => "Lithium",
            MaterialId.Uranium => "Uranium",
            MaterialId.Platinum => "Platinum",
            MaterialId.Gold => "Gold",
            _ => material.ToString(),
        };
    }
}
