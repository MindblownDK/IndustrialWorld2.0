// Assets/Scripts/VoxelEngine/GridSystem/GridSingularityHarvester.cs
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║                SINGULARITY HARVESTER — grid block (Phase 5)           ║
// ║                                                                       ║
// ║  The black hole is a destination now. This heavy grid block harvests  ║
// ║  SINGULARITY MATTER from the real singularity remnants:               ║
// ║                                                                       ║
// ║   • Only works in vacuum, on a powered grid.                          ║
// ║   • Harvest rate climbs the closer the block sits to the event        ║
// ║     horizon — the sweet spot is right outside the lethal zone.        ║
// ║   • The quasar (the supermassive variant) yields 1.5× per unit time,  ║
// ║     but its jets make parking there a death wish.                     ║
// ║   • Singularity Matter buffers internally and auto-pushes into grid   ║
// ║     cargo containers (same flow as the ship drill).                   ║
// ║   • The mini black hole inside the block spins its accretion disc.    ║
// ║                                                                       ║
// ║  Item / prefab / recipe / research are authored by Voxel Engine       ║
// ║  Setup Step 53 (non-destructive).                                     ║
// ╚══════════════════════════════════════════════════════════════════════╝
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public class GridSingularityHarvester : GridBlock, IGridDataProvider
    {
        [Header("Singularity Harvest")]
        [Tooltip("Horizon distance (km) within which harvesting is possible.")]
        public float harvestRangeKm = 2500f;

        [Tooltip("Singularity Matter produced per second at full efficiency.")]
        public float harvestRatePerSecond = 0.06f;

        [Tooltip("Power drawn (W) while actively harvesting.")]
        public float powerDrawWatts = 25000f;

        [Tooltip("Yield multiplier when harvesting the QUASAR (the supermassive remnant).")]
        public float quasarMultiplier = 1.5f;

        [Tooltip("The resource this block produces (wired by Setup Step 53).")]
        public ItemDefinition producedItem;

        [Header("Exotic Matter")]
        [Tooltip("Rare drop: harvested from BLACK HOLES. Containment-class — needs a Containment Vault.")]
        public ItemDefinition antimatterItem;

        [Tooltip("Rare drop: harvested from QUASARS. Containment-class — needs a Containment Vault.")]
        public ItemDefinition darkMatterItem;

        [Range(0f, 1f)]
        [Tooltip("Chance per produced unit of Singularity Matter that the black hole also yields 1 antimatter.")]
        public float antimatterDropChance = 0.10f;

        [Range(0f, 1f)]
        [Tooltip("Chance per produced unit of Singularity Matter that the quasar also yields 1 dark matter.")]
        public float darkMatterDropChance = 0.08f;

        [Tooltip("Disc spin speed of the contained mini black hole (deg/s).")]
        public float discSpinDegPerSecond = 18f;

        [Header("Buffer")]
        [Tooltip("Small internal buffer; auto-empties into grid cargo.")]
        public ItemContainer buffer;

        public const int BUFFER_SLOTS = 4;

        // ── Live state (terminal + LCD data provider) ──
        public bool IsHarvesting { get; private set; }
        public float Efficiency01 { get; private set; }
        public float HorizonDistanceKm { get; private set; } = float.MaxValue;
        public string NearestRemnant { get; private set; } = "—";
        public string Status { get; private set; } = "Idle";

        public override float PowerDraw => IsHarvesting ? powerDrawWatts : 0f;
        public override float ContentMass => buffer != null ? MassUtil.ContainerMass(buffer) : 0f;

        private float _accumulator;
        private float _pushTimer;
        private Transform _disc;
        private bool _rareBlocked;
        private Renderer[] _coilRenderers;
        private static readonly Color CoilColor = new Color(0.20f, 0.65f, 0.90f);

        public override void OnPlaced()
        {
            base.OnPlaced();
            blockName = "Singularity Harvester";
            if (buffer == null) buffer = new ItemContainer("Singularity Matter", BUFFER_SLOTS);
            else buffer.Resize(BUFFER_SLOTS);
            // The harvester's own buffer is containment-grade: it produces antimatter/dark
            // matter and must be able to hold them until a vault takes them.
            buffer.allowContainment = true;
            _disc = transform.Find("SingularityDisc");
        }

        private void Update()
        {
            // ── Power-driven visual drive: the contained black hole only spins and the
            // coils only breathe while the grid actually feeds the block. ──
            bool powered = Enabled && Grid != null && Grid.HasPower;
            if (_disc == null) _disc = transform.Find("SingularityDisc");
            if (_disc != null)
                _disc.Rotate(0f, discSpinDegPerSecond * Time.deltaTime * (powered ? 1f : 0.05f), 0f, Space.Self);

            if (_coilRenderers == null || _coilRenderers.Length == 0)
            {
                var coils = new System.Collections.Generic.List<Renderer>();
                foreach (Transform child in transform)
                    if (child.name == "CoilRing")
                    {
                        var r = child.GetComponent<Renderer>();
                        if (r != null) coils.Add(r);
                    }
                _coilRenderers = coils.ToArray();
            }
            float glow = powered ? 1.3f + 0.5f * Mathf.Sin(Time.unscaledTime * 3.6f) : 0.18f;
            for (int i = 0; i < _coilRenderers.Length; i++)
            {
                if (_coilRenderers[i] == null || _coilRenderers[i].material == null) continue;
                _coilRenderers[i].material.color = CoilColor * glow;
            }

            // Always try to offload the buffer, even when not actively harvesting.
            _pushTimer += Time.deltaTime;
            if (_pushTimer >= 0.5f) { _pushTimer = 0f; PushToCargo(); }
        }

        private void FixedUpdate()
        {
            IsHarvesting = false;
            Efficiency01 = 0f;

            if (!Enabled) { Status = "Disabled"; return; }
            if (Grid == null) { Status = "No Grid"; return; }
            if (!AtmosphereManager.IsInSpace(transform.position)) { Status = "Requires vacuum"; return; }
            if (!Grid.HasPower) { Status = "No Power"; return; }

            var registry = CosmicRegistry.Instance;
            var origin = SpaceOrigin.Instance;
            if (registry == null || !registry.IsReady || origin == null) { Status = "No star map"; return; }

            double3 cosmic = origin.GetCosmicKm(transform.position);

            // Nearest singularity remnant (black hole preferred order is irrelevant —
            // closest horizon distance wins; the quasar applies its own multiplier).
            SingularityInstance nearest = null;
            double nearestR = double.MaxValue;
            if (registry.Singularities != null)
            {
                for (int i = 0; i < registry.Singularities.Count; i++)
                {
                    var s = registry.Singularities[i];
                    if (s == null) continue;
                    double r = s.HorizonDistanceKm(cosmic);
                    if (r < nearestR) { nearestR = r; nearest = s; }
                }
            }

            if (nearest == null || nearestR >= harvestRangeKm)
            {
                HorizonDistanceKm = float.MaxValue;
                NearestRemnant = "—";
                Status = "No singularity in range";
                return;
            }

            HorizonDistanceKm = (float)nearestR;
            NearestRemnant = nearest.DisplayName;

            // Danger = reward: efficiency climbs from 25% at the range edge to 100%
            // at the horizon — the best yield sits right outside the lethal zone.
            Efficiency01 = Mathf.Clamp01(0.25f + 0.75f * (1f - (float)nearestR / harvestRangeKm));
            float multiplier = nearest.kind == SingularityKind.Quasar ? quasarMultiplier : 1f;

            if (producedItem == null)
            {
                Status = "No output item (run Step 53)";
                return;
            }
            if (IsBufferFull())
            {
                Status = "Buffer full — connect cargo";
                return;
            }

            IsHarvesting = true;
            Status = $"Harvesting {nearest.DisplayName} ({HorizonDistanceKm:0} km to horizon)";

            _accumulator += harvestRatePerSecond * Efficiency01 * multiplier * Time.fixedDeltaTime;
            if (_accumulator >= 1f)
            {
                int units = Mathf.FloorToInt(_accumulator);
                var leftover = buffer.Insert(new ItemStack(producedItem, units));
                _accumulator = Mathf.Max(0f, _accumulator - units) + (leftover != null ? leftover.count : 0);

                // Exotic matter: black holes shed ANTIMATTER, quasars shed DARK MATTER.
                // Rolled per produced unit — the rarest loot in the system.
                bool isQuasar = nearest.kind == SingularityKind.Quasar;
                ItemDefinition exotic = isQuasar ? darkMatterItem : antimatterItem;
                float chance = isQuasar ? darkMatterDropChance : antimatterDropChance;
                int exoticCount = 0;
                for (int i = 0; i < units && exotic != null; i++)
                    if (UnityEngine.Random.value < chance) exoticCount++;
                if (exoticCount > 0 && exotic != null)
                    buffer.Insert(new ItemStack(exotic, exoticCount));
            }

            if (_rareBlocked)
                Status = "No containment vault — exotic matter buffered";
        }

        private bool IsBufferFull()
        {
            if (buffer == null) return true;
            for (int i = 0; i < buffer.Size; i++)
            {
                var slot = buffer.GetSlot(i);
                if (slot == null || slot.IsEmpty) return false;
            }
            return true;
        }

        // Empty the buffer into grid containers. Ordinary matter goes to any cargo;
        // containment-class items (antimatter/dark matter) are REFUSED by plain cargo
        // and only the Containment Vault accepts them — so the same push loop works:
        // rejected stacks simply stay in the buffer.
        private void PushToCargo()
        {
            if (buffer == null || Grid == null || GridItemNetwork.Instance == null) return;
            var cargos = GridItemNetwork.Instance.GetConnectedContainers(Grid);
            if (cargos.Count == 0) return;

            bool exoticStuck = false;

            for (int i = 0; i < buffer.Size; i++)
            {
                var s = buffer.GetSlot(i);
                if (s == null || s.IsEmpty || s.item == null) continue;
                var moving = new ItemStack(s.item, s.count);
                foreach (var cargo in cargos)
                {
                    if (cargo?.container == null) continue;
                    moving = cargo.container.Insert(moving);
                    if (moving == null || moving.IsEmpty) break;
                }
                int moved = s.count - (moving?.count ?? 0);
                if (moved > 0) buffer.Remove(s.item, moved);
                if (moving != null && !moving.IsEmpty && s.item.requiresContainment)
                    exoticStuck = true;
            }

            _rareBlocked = exoticStuck;
        }

        // ── LCD data provider ─────────────────────────────────────
        public string SourceName => blockName;
        public string DataCategory => "Singularity";
        public string GetDisplayData()
        {
            string range = HorizonDistanceKm >= float.MaxValue
                ? "— km"
                : $"{HorizonDistanceKm:0} km to horizon";
            int exotic = 0;
            if (buffer != null)
            {
                for (int i = 0; i < buffer.Size; i++)
                {
                    var s = buffer.GetSlot(i);
                    if (s != null && !s.IsEmpty && s.item != null && s.item.requiresContainment)
                        exotic += s.count;
                }
            }
            return $"SINGULARITY HARVESTER\n{Status}\nEfficiency {Efficiency01 * 100f:0}%\nRange {range}\nTarget {NearestRemnant}\nExotic buffered {exotic}";
        }
    }
}
