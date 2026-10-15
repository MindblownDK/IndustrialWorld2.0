// Assets/Scripts/VoxelEngine/GridSystem/GridCargoDropHatch.cs
//
// Grid-mounted Cargo Drop Hatch (17.24.0-dev).
// Allows a ship, hovercraft, rover, or station grid to export cargo without docking:
//   - Auto-pulls matching items from the grid's containers into its own internal buffer.
//   - Provides 4 user-configurable multi-item filters (Filter 1..4). Only one filter
//     can be active at a time (or all off), toggleable from both the block UI and the
//     cockpit hotbar (GridControlHud).
//   - Features a multi-stage clam-shell cargo bay door + locking latch + hydraulic
//     piston animation when opening, then drops buffered items downward into the world
//     (and into any ReceivingChute / GridReceivingChute below).

using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    [Serializable]
    public class DropHatchFilterPreset
    {
        public string label = "Filter";
        public bool matchAllItems;
        public List<ItemDefinition> items = new();
        [HideInInspector] public List<string> savedItemIds = new();

        public void EnsureInitialized(int index)
        {
            if (string.IsNullOrWhiteSpace(label))
                label = $"Filter {index + 1}";
            items ??= new List<ItemDefinition>();
            savedItemIds ??= new List<string>();
        }

        public bool Matches(ItemDefinition item)
        {
            if (item == null) return false;
            if (matchAllItems) return true;
            ResolveSavedIds();
            if (items == null || items.Count == 0) return false;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] == item) return true;
            }
            return false;
        }

        public void AddItem(ItemDefinition item)
        {
            if (item == null) return;
            EnsureInitialized(0);
            ResolveSavedIds();
            if (!items.Contains(item))
                items.Add(item);
            SyncSavedIds();
        }

        public void RemoveItem(ItemDefinition item)
        {
            if (item == null) return;
            EnsureInitialized(0);
            ResolveSavedIds();
            items.Remove(item);
            SyncSavedIds();
        }

        public void Clear()
        {
            EnsureInitialized(0);
            items.Clear();
            savedItemIds.Clear();
            matchAllItems = false;
        }

        public void SyncSavedIds()
        {
            savedItemIds ??= new List<string>();
            savedItemIds.Clear();
            if (items == null) return;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it != null && !string.IsNullOrEmpty(it.itemId) && !savedItemIds.Contains(it.itemId))
                    savedItemIds.Add(it.itemId);
            }
        }

        public void ResolveSavedIds()
        {
            if (savedItemIds == null || savedItemIds.Count == 0) return;
            items ??= new List<ItemDefinition>();
            if (items.Count > 0) return;

            var all = Resources.FindObjectsOfTypeAll<ItemDefinition>();
            for (int i = 0; i < savedItemIds.Count; i++)
            {
                string id = savedItemIds[i];
                if (string.IsNullOrEmpty(id)) continue;
                for (int j = 0; j < all.Length; j++)
                {
                    if (all[j] != null && all[j].itemId == id)
                    {
                        if (!items.Contains(all[j]))
                            items.Add(all[j]);
                        break;
                    }
                }
            }
        }

        public string SummaryText()
        {
            ResolveSavedIds();
            if (matchAllItems) return "All Items";
            if (items == null || items.Count == 0) return "Empty (No items set)";
            if (items.Count == 1 && items[0] != null) return items[0].displayName;
            if (items.Count == 2 && items[0] != null && items[1] != null)
                return $"{items[0].displayName}, {items[1].displayName}";
            return $"{items[0]?.displayName ?? "Item"} +{items.Count - 1} more";
        }
    }

    public class GridCargoDropHatch : GridBlock, IGridItemStore, IGridDataProvider, IMachineProcessState
    {
        public const int FilterCount = 4;

        [Header("Cargo Buffer")]
        public int slots = 12;
        public float maxMassKg = 50_000f;
        public ItemContainer container;

        [Header("Auto-Pull & 4 Toggleable Filters")]
        [Tooltip("When enabled, automatically pulls items matching the active filter from all containers on the same grid into this hatch.")]
        public bool autoPullFromGrid = true;
        [Tooltip("Index of the currently active filter (0..3), or -1 when all filters are toggled off. Only one filter can be active at once.")]
        public int activeFilterIndex = -1;
        public DropHatchFilterPreset[] filters = new DropHatchFilterPreset[FilterCount];

        [Header("Hatch Door & Drop Settings")]
        public bool hatchOpen;
        [Tooltip("When enabled, choosing one of the 4 filters automatically opens the hatch bay doors (and turning filters off closes them). Default is OFF.")]
        public bool autoOpenWithFilter = false;
        [Tooltip("When true, only drops items that match the active filter (if a filter is active). When false, drops any item in the hatch buffer while open.")]
        public bool dropOnlyMatchingWhenFiltered = true;
        public float pullInterval = 0.25f;
        public float dropInterval = 0.20f;
        public int maxDropPerCycle = 16;
        public float animationSpeed = 2.6f;

        // Runtime state
        private float _open01;
        private float _pullTimer;
        private float _dropTimer;
        private int _totalDroppedCount;

        // Cached procedural animation transforms
        private Transform _leftDoorPivot;
        private Transform _rightDoorPivot;
        private Transform _frontLatch;
        private Transform _rearLatch;
        private Transform _leftPiston;
        private Transform _rightPiston;
        private Renderer _beaconLeft;
        private Renderer _beaconRight;
        private MaterialPropertyBlock _mpb;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        public ItemContainer ItemStore => container;
        public string StoreLabel => string.IsNullOrWhiteSpace(blockName) || blockName == "Armor Block"
            ? "Cargo Drop Hatch"
            : blockName;
        public float Open01 => _open01;
        public int TotalDroppedCount => _totalDroppedCount;
        public float CurrentMassKg => MassUtil.ContainerMass(container);
        public float Fill01 => maxMassKg > 0.001f ? Mathf.Clamp01(CurrentMassKg / maxMassKg) : 0f;
        public override float ContentMass => CurrentMassKg;

        public DropHatchFilterPreset ActiveFilter
            => activeFilterIndex >= 0 && activeFilterIndex < FilterCount ? GetFilter(activeFilterIndex) : null;

        public DropHatchFilterPreset GetFilter(int index)
        {
            EnsureFilters();
            index = Mathf.Clamp(index, 0, FilterCount - 1);
            return filters[index];
        }

        public void EnsureFilters()
        {
            if (filters == null || filters.Length != FilterCount)
            {
                var next = new DropHatchFilterPreset[FilterCount];
                for (int i = 0; i < FilterCount; i++)
                {
                    next[i] = (filters != null && i < filters.Length && filters[i] != null)
                        ? filters[i]
                        : new DropHatchFilterPreset { label = $"Filter {i + 1}" };
                }
                filters = next;
            }
            for (int i = 0; i < FilterCount; i++)
            {
                filters[i] ??= new DropHatchFilterPreset { label = $"Filter {i + 1}" };
                filters[i].EnsureInitialized(i);
            }
            activeFilterIndex = Mathf.Clamp(activeFilterIndex, -1, FilterCount - 1);
        }

        public void EnsureContainers()
        {
            EnsureFilters();
            if (container == null)
            {
                container = new ItemContainer("Drop Hatch", Mathf.Max(1, slots));
                container.OnChanged += OnContainerChanged;
            }
            else if (container.Size != Mathf.Max(1, slots))
            {
                container.Resize(Mathf.Max(1, slots));
            }
            container.OverrideMaxWeightKg = maxMassKg;
            container.allowContainment = false;
        }

        private void Awake()
        {
            EnsureFilters();
            EnsureVisuals();
            CacheAnimationParts();
        }

        public override void OnPlaced()
        {
            EnsureContainers();
            EnsureVisuals();
            CacheAnimationParts();
            ApplyDoorPose(_open01);
            if (Grid != null)
                GridItemNetwork.Instance.RegisterStore(Grid, this);
        }

        public override void OnRemoved()
        {
            if (container != null)
                container.OnChanged -= OnContainerChanged;
            if (Grid != null)
                GridItemNetwork.Instance.UnregisterStore(Grid, this);
        }

        private void OnContainerChanged()
        {
            if (Grid != null) Grid.RecalculateMass();
        }

        /// <summary>
        /// Toggles one of the 4 filters (0..3). Only one filter can be active at once:
        /// activating Filter N deactivates all others; toggling the already-active filter
        /// turns filtering off (-1).
        /// </summary>
        public void ToggleFilter(int filterIdx)
        {
            EnsureFilters();
            if (filterIdx < 0 || filterIdx >= FilterCount)
            {
                SetActiveFilter(-1);
                return;
            }
            int next = (activeFilterIndex == filterIdx) ? -1 : filterIdx;
            SetActiveFilter(next);
        }

        public void SetActiveFilter(int filterIdx)
        {
            EnsureFilters();
            activeFilterIndex = (filterIdx >= 0 && filterIdx < FilterCount) ? filterIdx : -1;
            if (autoOpenWithFilter)
                hatchOpen = activeFilterIndex >= 0;

            VoxelEngine.Networking.GridStateSync.NotifyLocalInteraction(this);
            VoxelEngine.UI.GameUIController.Instance?.RequestRefresh();
        }

        public void SetAutoOpenWithFilter(bool enable)
        {
            autoOpenWithFilter = enable;
            if (autoOpenWithFilter && activeFilterIndex >= 0)
                hatchOpen = true;

            VoxelEngine.Networking.GridStateSync.NotifyLocalInteraction(this);
            VoxelEngine.UI.GameUIController.Instance?.RequestRefresh();
        }

        public void ToggleAutoOpenWithFilter() => SetAutoOpenWithFilter(!autoOpenWithFilter);

        public void CycleActiveFilter()
        {
            EnsureFilters();
            int next = activeFilterIndex + 1;
            if (next >= FilterCount) next = -1;
            SetActiveFilter(next);
        }

        public void SetHatchOpen(bool open)
        {
            hatchOpen = open;
            VoxelEngine.Networking.GridStateSync.NotifyLocalInteraction(this);
            VoxelEngine.UI.GameUIController.Instance?.RequestRefresh();
        }

        public void ToggleHatchOpen() => SetHatchOpen(!hatchOpen);

        public bool MatchesActiveFilter(ItemDefinition item)
        {
            if (item == null) return false;
            var active = ActiveFilter;
            if (active == null) return false;
            return active.Matches(item);
        }

        private void Update()
        {
            EnsureContainers();
            if (_leftDoorPivot == null || _rightDoorPivot == null)
                CacheAnimationParts();

            // 1. Animate clam-shell bay doors & locking latches.
            float targetOpen = (Enabled && hatchOpen) ? 1f : 0f;
            if (!Mathf.Approximately(_open01, targetOpen))
            {
                _open01 = Mathf.MoveTowards(_open01, targetOpen, Mathf.Max(0.5f, animationSpeed) * Time.deltaTime);
                ApplyDoorPose(_open01);
            }
            UpdateBeaconColor();

            if (!Enabled) return;

            // 2. Auto-pull matching items from the grid's containers into our internal buffer.
            if (autoPullFromGrid && activeFilterIndex >= 0 && Grid != null)
            {
                _pullTimer += Time.deltaTime;
                if (_pullTimer >= Mathf.Max(0.05f, pullInterval))
                {
                    _pullTimer = 0f;
                    PullMatchingFromGrid();
                }
            }

            // 3. Drop items downward once the bay doors have swung past 65% open.
            if (hatchOpen && _open01 >= 0.65f && container != null && !container.IsEmpty())
            {
                _dropTimer += Time.deltaTime;
                if (_dropTimer >= Mathf.Max(0.05f, dropInterval))
                {
                    _dropTimer = 0f;
                    DropNextStackDownward();
                }
            }
        }

        private void PullMatchingFromGrid()
        {
            if (Grid == null || container == null) return;
            var filter = ActiveFilter;
            if (filter == null) return;

            var stores = GridItemNetwork.Instance.GetStores(Grid);
            for (int s = 0; s < stores.Count; s++)
            {
                var store = stores[s];
                if (store == null || ReferenceEquals(store, this)) continue;
                var src = store.ItemStore;
                if (src == null || ReferenceEquals(src, container)) continue;
                if (PullFromContainer(src, filter)) return;
            }

            // Also scan GridDrill / GridElectricFurnace outputs on the same grid if not registered in stores.
            foreach (var b in Grid.AllBlocks)
            {
                if (b == null || ReferenceEquals(b, this)) continue;
                if (b is GridDrill drill && drill.buffer != null && PullFromContainer(drill.buffer, filter)) return;
                if (b is GridElectricFurnace ef && ef.outputC != null && PullFromContainer(ef.outputC, filter)) return;
            }
        }

        private bool PullFromContainer(ItemContainer src, DropHatchFilterPreset filter)
        {
            if (src == null || filter == null) return false;
            for (int i = 0; i < src.Size; i++)
            {
                var st = src.GetSlot(i);
                if (st == null || st.IsEmpty || st.item == null) continue;
                if (!filter.Matches(st.item)) continue;

                int toPull = Mathf.Min(st.count, Mathf.Max(1, maxDropPerCycle));
                var attempt = st.Clone();
                attempt.count = toPull;

                var leftover = container.Insert(attempt);
                int moved = toPull - (leftover?.count ?? 0);
                if (moved <= 0) continue;

                st.count -= moved;
                src.SetSlot(i, st.count > 0 ? st : new ItemStack());
                return true;
            }
            return false;
        }

        private void DropNextStackDownward()
        {
            if (container == null) return;
            var filter = ActiveFilter;

            for (int i = 0; i < container.Size; i++)
            {
                var st = container.GetSlot(i);
                if (st == null || st.IsEmpty || st.item == null) continue;
                if (dropOnlyMatchingWhenFiltered && filter != null && !filter.Matches(st.item))
                    continue;

                int dropCount = Mathf.Min(st.count, Mathf.Max(1, maxDropPerCycle));
                int capacity = DroppedItem.AvailablePhysicalCapacity;
                if (capacity <= 0) return;
                dropCount = Mathf.Min(dropCount, capacity);
                if (dropCount <= 0) return;

                Vector3 downDir = -transform.up;
                if (downDir.sqrMagnitude < 0.001f) downDir = Vector3.down;
                downDir.Normalize();

                float clearance = (Grid != null && Grid.gridSize == GridSize.Small) ? 0.52f : 1.45f;
                Vector3 spawnPos = transform.position + downDir * clearance;

                var spawnStack = st.Clone();
                spawnStack.count = dropCount;

                var dropped = DroppedItem.Spawn(spawnStack, spawnPos, downDir);
                if (dropped == null) return;

                // Give the dropped item a crisp downward ejection velocity out of the bay doors.
                var rb = dropped.GetComponent<Rigidbody>();
                if (rb != null && !rb.isKinematic)
                {
                    Vector3 shipVel = (Grid != null && Grid.Body != null) ? Grid.Body.linearVelocity : Vector3.zero;
                    rb.linearVelocity = shipVel + downDir * 5.5f;
                }

                int actualDropped = dropped.stack != null ? dropped.stack.count : dropCount;
                _totalDroppedCount += actualDropped;
                st.count -= actualDropped;
                container.SetSlot(i, st.count > 0 ? st : new ItemStack());
                return;
            }
        }

        // ── Procedural Clam-Shell Bay Door Visuals & Animation ──────────────────
        public void EnsureVisuals()
        {
            var existingRoot = transform.Find("Generated_HatchVisuals");
            if (existingRoot != null) return;

            // Hide default single-cube placeholder mesh on root if present so the open throat is visible.
            var rootMr = GetComponent<MeshRenderer>();
            if (rootMr != null) rootMr.enabled = false;

            var root = new GameObject("Generated_HatchVisuals").transform;
            root.SetParent(transform, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            Color frameCol = new Color(0.20f, 0.23f, 0.27f);
            Color darkThroatCol = new Color(0.09f, 0.10f, 0.12f);
            Color doorCol = new Color(0.28f, 0.31f, 0.36f);
            Color hazardCol = new Color(0.92f, 0.68f, 0.12f);
            Color pistonCol = new Color(0.62f, 0.66f, 0.72f);

            // Outer structural frame beams around the perimeter so the center drop bay is open
            CreatePart(root, "Frame_North", new Vector3(0f, 0f, 0.41f), new Vector3(0.98f, 0.36f, 0.16f), frameCol);
            CreatePart(root, "Frame_South", new Vector3(0f, 0f, -0.41f), new Vector3(0.98f, 0.36f, 0.16f), frameCol);
            CreatePart(root, "Frame_West", new Vector3(-0.41f, 0f, 0f), new Vector3(0.16f, 0.36f, 0.66f), frameCol);
            CreatePart(root, "Frame_East", new Vector3(0.41f, 0f, 0f), new Vector3(0.16f, 0.36f, 0.66f), frameCol);

            // Upper intake funnel collar
            CreatePart(root, "UpperCollar_North", new Vector3(0f, 0.22f, 0.36f), new Vector3(0.84f, 0.14f, 0.10f), darkThroatCol);
            CreatePart(root, "UpperCollar_South", new Vector3(0f, 0.22f, -0.36f), new Vector3(0.84f, 0.14f, 0.10f), darkThroatCol);
            CreatePart(root, "UpperCollar_West", new Vector3(-0.36f, 0.22f, 0f), new Vector3(0.10f, 0.14f, 0.62f), darkThroatCol);
            CreatePart(root, "UpperCollar_East", new Vector3(0.36f, 0.22f, 0f), new Vector3(0.10f, 0.14f, 0.62f), darkThroatCol);

            // Hazard warning trim on underside edges
            CreatePart(root, "Hazard_North", new Vector3(0f, -0.17f, 0.45f), new Vector3(0.92f, 0.04f, 0.06f), hazardCol);
            CreatePart(root, "Hazard_South", new Vector3(0f, -0.17f, -0.45f), new Vector3(0.92f, 0.04f, 0.06f), hazardCol);

            // Status beacons on underside
            var bL = CreatePart(root, "Generated_BeaconLeft", new Vector3(-0.42f, -0.17f, 0f), new Vector3(0.05f, 0.04f, 0.44f), hazardCol);
            var bR = CreatePart(root, "Generated_BeaconRight", new Vector3(0.42f, -0.17f, 0f), new Vector3(0.05f, 0.04f, 0.44f), hazardCol);
            _beaconLeft = bL.GetComponent<Renderer>();
            _beaconRight = bR.GetComponent<Renderer>();

            // Retractable locking latches (slide along Z before doors swing open)
            _frontLatch = CreatePart(root, "Generated_FrontLatch", new Vector3(0f, -0.15f, 0.24f), new Vector3(0.14f, 0.05f, 0.14f), hazardCol);
            _rearLatch = CreatePart(root, "Generated_RearLatch", new Vector3(0f, -0.15f, -0.24f), new Vector3(0.14f, 0.05f, 0.14f), hazardCol);

            // Hydraulic actuator rods
            _leftPiston = CreatePart(root, "Generated_LeftPiston", new Vector3(-0.31f, 0.02f, 0f), new Vector3(0.06f, 0.24f, 0.08f), pistonCol);
            _rightPiston = CreatePart(root, "Generated_RightPiston", new Vector3(0.31f, 0.02f, 0f), new Vector3(0.06f, 0.24f, 0.08f), pistonCol);

            // Left Clam-Shell Bomb-Bay Door Pivot (hinged at X = -0.33, Y = -0.14)
            var leftPivot = new GameObject("Generated_LeftBayDoorPivot").transform;
            leftPivot.SetParent(root, false);
            leftPivot.localPosition = new Vector3(-0.33f, -0.14f, 0f);
            leftPivot.localRotation = Quaternion.identity;
            CreatePart(leftPivot, "LeftDoorLeaf", new Vector3(0.165f, 0f, 0f), new Vector3(0.33f, 0.05f, 0.64f), doorCol);
            CreatePart(leftPivot, "LeftDoorLip", new Vector3(0.31f, -0.01f, 0f), new Vector3(0.04f, 0.06f, 0.62f), hazardCol);
            _leftDoorPivot = leftPivot;

            // Right Clam-Shell Bomb-Bay Door Pivot (hinged at X = +0.33, Y = -0.14)
            var rightPivot = new GameObject("Generated_RightBayDoorPivot").transform;
            rightPivot.SetParent(root, false);
            rightPivot.localPosition = new Vector3(0.33f, -0.14f, 0f);
            rightPivot.localRotation = Quaternion.identity;
            CreatePart(rightPivot, "RightDoorLeaf", new Vector3(-0.165f, 0f, 0f), new Vector3(0.33f, 0.05f, 0.64f), doorCol);
            CreatePart(rightPivot, "RightDoorLip", new Vector3(-0.31f, -0.01f, 0f), new Vector3(0.04f, 0.06f, 0.62f), hazardCol);
            _rightDoorPivot = rightPivot;
        }

        public void RebuildVisuals()
        {
            var existing = transform.Find("Generated_HatchVisuals");
            if (existing != null)
            {
                if (Application.isPlaying) Destroy(existing.gameObject);
                else DestroyImmediate(existing.gameObject);
            }
            EnsureVisuals();
        }

        private static Transform CreatePart(Transform parent, string name, Vector3 localPos, Vector3 localScale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && Application.isPlaying)
            {
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor(BaseColorId, color);
                mpb.SetColor(ColorId, color);
                mr.SetPropertyBlock(mpb);
            }
            return go.transform;
        }

        private void CacheAnimationParts()
        {
            var root = transform.Find("Generated_HatchVisuals");
            if (root == null) return;
            _leftDoorPivot = root.Find("Generated_LeftBayDoorPivot");
            _rightDoorPivot = root.Find("Generated_RightBayDoorPivot");
            _frontLatch = root.Find("Generated_FrontLatch");
            _rearLatch = root.Find("Generated_RearLatch");
            _leftPiston = root.Find("Generated_LeftPiston");
            _rightPiston = root.Find("Generated_RightPiston");
            var bL = root.Find("Generated_BeaconLeft");
            var bR = root.Find("Generated_BeaconRight");
            if (bL != null) _beaconLeft = bL.GetComponent<Renderer>();
            if (bR != null) _beaconRight = bR.GetComponent<Renderer>();
        }

        private void ApplyDoorPose(float t01)
        {
            float clamped = Mathf.Clamp01(t01);
            // Stage 1 (0..0.28): locking latches slide outward to release the bay doors.
            float latchT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(clamped / 0.28f));
            if (_frontLatch != null)
                _frontLatch.localPosition = new Vector3(0f, -0.15f, Mathf.Lerp(0.24f, 0.36f, latchT));
            if (_rearLatch != null)
                _rearLatch.localPosition = new Vector3(0f, -0.15f, Mathf.Lerp(-0.24f, -0.36f, latchT));

            // Stage 2 (0.15..1.0): twin clam-shell bay doors swing downward & outward to 78 degrees.
            // In Unity left-handed coords: left leaf (+X from hinge) swings downward (-Y) with -Z rotation;
            // right leaf (-X from hinge) swings downward (-Y) with +Z rotation.
            float doorT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((clamped - 0.15f) / 0.85f));
            float angle = doorT * 78f;
            if (_leftDoorPivot != null)
                _leftDoorPivot.localRotation = Quaternion.Euler(0f, 0f, -angle);
            if (_rightDoorPivot != null)
                _rightDoorPivot.localRotation = Quaternion.Euler(0f, 0f, angle);

            // Hydraulic pistons extend downward as doors open.
            if (_leftPiston != null)
                _leftPiston.localPosition = new Vector3(-0.31f, Mathf.Lerp(0.02f, -0.09f, doorT), 0f);
            if (_rightPiston != null)
                _rightPiston.localPosition = new Vector3(0.31f, Mathf.Lerp(0.02f, -0.09f, doorT), 0f);
        }

        private void UpdateBeaconColor()
        {
            if (_beaconLeft == null && _beaconRight == null) return;
            _mpb ??= new MaterialPropertyBlock();

            Color c = !Enabled ? new Color(0.45f, 0.15f, 0.15f)
                : (hatchOpen && activeFilterIndex >= 0) ? new Color(0.20f, 0.95f, 0.45f)
                : hatchOpen ? new Color(0.95f, 0.72f, 0.18f)
                : new Color(0.25f, 0.55f, 0.75f);

            _mpb.SetColor(BaseColorId, c);
            _mpb.SetColor(ColorId, c);
            _mpb.SetColor(EmissionColorId, c * 1.5f);
            if (_beaconLeft != null) _beaconLeft.SetPropertyBlock(_mpb);
            if (_beaconRight != null) _beaconRight.SetPropertyBlock(_mpb);
        }

        // ── Persistence (IMachineProcessState) ──────────────────────────────────
        public void CaptureProcessState(MachineProcessState state)
        {
            if (state == null) return;
            EnsureFilters();
            state.SetExtra("hatchOpen", hatchOpen ? 1f : 0f);
            state.SetExtra("autoOpenWithFilter", autoOpenWithFilter ? 1f : 0f);
            state.SetExtra("autoPullFromGrid", autoPullFromGrid ? 1f : 0f);
            state.SetExtra("dropOnlyMatching", dropOnlyMatchingWhenFiltered ? 1f : 0f);
            state.SetExtra("activeFilterIndex", activeFilterIndex);
            state.SetExtra("totalDropped", _totalDroppedCount);

            // Encode the 4 filter presets into state.recipeName as compact pipe-delimited string
            // so all item IDs and labels persist cleanly alongside numeric extras.
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < FilterCount; i++)
            {
                var f = filters[i];
                f.SyncSavedIds();
                if (i > 0) sb.Append("||");
                sb.Append((f.label ?? $"Filter {i + 1}").Replace("|", "/").Replace(";", ","));
                sb.Append(';');
                sb.Append(f.matchAllItems ? '1' : '0');
                sb.Append(';');
                sb.Append(string.Join(",", f.savedItemIds));
            }
            state.recipeName = sb.ToString();
        }

        public void RestoreProcessState(MachineProcessState state)
        {
            if (state == null) return;
            EnsureFilters();
            hatchOpen = state.GetExtra("hatchOpen", hatchOpen ? 1f : 0f) >= 0.5f;
            autoOpenWithFilter = state.GetExtra("autoOpenWithFilter", 0f) >= 0.5f;
            autoPullFromGrid = state.GetExtra("autoPullFromGrid", autoPullFromGrid ? 1f : 0f) >= 0.5f;
            dropOnlyMatchingWhenFiltered = state.GetExtra("dropOnlyMatching", dropOnlyMatchingWhenFiltered ? 1f : 0f) >= 0.5f;
            activeFilterIndex = Mathf.Clamp(Mathf.RoundToInt(state.GetExtra("activeFilterIndex", -1f)), -1, FilterCount - 1);
            _totalDroppedCount = Mathf.Max(0, Mathf.RoundToInt(state.GetExtra("totalDropped", 0f)));

            if (!string.IsNullOrEmpty(state.recipeName))
            {
                string[] parts = state.recipeName.Split(new[] { "||" }, StringSplitOptions.None);
                for (int i = 0; i < Mathf.Min(FilterCount, parts.Length); i++)
                {
                    string[] fields = parts[i].Split(';');
                    if (fields.Length >= 1 && !string.IsNullOrWhiteSpace(fields[0]))
                        filters[i].label = fields[0];
                    if (fields.Length >= 2)
                        filters[i].matchAllItems = fields[1] == "1";
                    filters[i].items.Clear();
                    filters[i].savedItemIds.Clear();
                    if (fields.Length >= 3 && !string.IsNullOrEmpty(fields[2]))
                    {
                        string[] ids = fields[2].Split(',');
                        for (int k = 0; k < ids.Length; k++)
                        {
                            if (!string.IsNullOrWhiteSpace(ids[k]))
                                filters[i].savedItemIds.Add(ids[k].Trim());
                        }
                        filters[i].ResolveSavedIds();
                    }
                }
            }

            _open01 = (Enabled && hatchOpen) ? 1f : 0f;
            ApplyDoorPose(_open01);
        }

        // ── IGridDataProvider ───────────────────────────────────────────────────
        public string SourceName => StoreLabel;
        public string DataCategory => "Logistics";

        public string GetDisplayData()
        {
            EnsureContainers();
            string activeLabel = activeFilterIndex >= 0
                ? $"F{activeFilterIndex + 1} ({GetFilter(activeFilterIndex).label})"
                : "OFF";
            return $"DROP HATCH\nDoor: {(hatchOpen ? "OPEN" : "CLOSED")} (Auto-Open: {(autoOpenWithFilter ? "ON" : "OFF")})\nFilter: {activeLabel}\nDropped: {_totalDroppedCount:N0}";
        }
    }
}
