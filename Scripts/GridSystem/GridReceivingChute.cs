// Assets/Scripts/VoxelEngine/GridSystem/GridReceivingChute.cs
//
// Grid-mounted Large Receiving Chute / Catch Hopper (17.24.0-dev).
// Mounts on ships, barges, haulers, rovers, or station grids to catch physical
// DroppedItems falling from above (such as from another ship's GridCargoDropHatch).
// Features a wide flared catch rim, a generous capture box above the funnel mouth,
// a 16-slot internal buffer, and optional automatic forwarding into the grid's
// GridCargoContainers via GridItemNetwork.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public class GridReceivingChute : GridBlock, IGridItemStore, IGridDataProvider, IMachineProcessState
    {
        [Header("Hopper Buffer & Capture")]
        public int slots = 16;
        public float maxMassKg = 100_000f;
        public ItemContainer container;
        [Tooltip("When enabled, automatically forwards captured items into other GridCargoContainers on the same grid.")]
        public bool autoPushToGridCargo = true;
        public Vector3 captureHalfExtents = new Vector3(1.85f, 1.75f, 1.85f);
        public Vector3 captureCenterOffset = new Vector3(0f, 1.15f, 0f);
        public float captureScanInterval = 0.08f;
        public float pushInterval = 0.20f;

        private float _scanTimer;
        private float _pushTimer;
        private int _totalCapturedCount;
        private static readonly Collider[] OverlapBuf = new Collider[64];
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public ItemContainer ItemStore => container;
        public string StoreLabel => string.IsNullOrWhiteSpace(blockName) || blockName == "Armor Block"
            ? "Grid Receiving Chute"
            : blockName;
        public int TotalCapturedCount => _totalCapturedCount;
        public float CurrentMassKg => MassUtil.ContainerMass(container);
        public float Fill01 => maxMassKg > 0.001f ? Mathf.Clamp01(CurrentMassKg / maxMassKg) : 0f;
        public override float ContentMass => CurrentMassKg;

        public void EnsureContainers()
        {
            if (container == null)
            {
                container = new ItemContainer("Receiving Chute", Mathf.Max(1, slots));
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
            EnsureContainers();
            EnsureVisuals();
            EnsureCaptureTrigger();
        }

        public override void OnPlaced()
        {
            EnsureContainers();
            EnsureVisuals();
            EnsureCaptureTrigger();
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

        private void Update()
        {
            if (!Enabled) return;
            EnsureContainers();

            _scanTimer += Time.deltaTime;
            if (_scanTimer >= Mathf.Max(0.04f, captureScanInterval))
            {
                _scanTimer = 0f;
                ScanAndCaptureDroppedItems();
            }

            if (autoPushToGridCargo && Grid != null && !container.IsEmpty())
            {
                _pushTimer += Time.deltaTime;
                if (_pushTimer >= Mathf.Max(0.05f, pushInterval))
                {
                    _pushTimer = 0f;
                    PushToGridCargoContainers();
                }
            }
        }

        private void ScanAndCaptureDroppedItems()
        {
            Vector3 center = transform.TransformPoint(captureCenterOffset);
            Vector3 worldHalfExtents = Vector3.Scale(captureHalfExtents, transform.lossyScale);
            worldHalfExtents.x = Mathf.Max(1.65f, Mathf.Abs(worldHalfExtents.x));
            worldHalfExtents.y = Mathf.Max(1.55f, Mathf.Abs(worldHalfExtents.y));
            worldHalfExtents.z = Mathf.Max(1.65f, Mathf.Abs(worldHalfExtents.z));

            int count = Physics.OverlapBoxNonAlloc(
                center,
                worldHalfExtents,
                OverlapBuf,
                transform.rotation,
                ~0,
                QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                var col = OverlapBuf[i];
                OverlapBuf[i] = null;
                if (col == null) continue;
                var dropped = col.GetComponentInParent<DroppedItem>();
                if (dropped == null || dropped.stack == null || dropped.stack.IsEmpty) continue;

                TryCollectDroppedItem(dropped);
            }
        }

        public bool TryCollectDroppedItem(DroppedItem dropped)
        {
            if (!Enabled || dropped == null || dropped.stack == null || dropped.stack.IsEmpty || dropped.stack.item == null)
                return false;

            EnsureContainers();
            var incoming = dropped.stack.Clone();
            var leftover = container.Insert(incoming);
            int accepted = dropped.stack.count - (leftover?.count ?? 0);
            if (accepted <= 0) return false;

            dropped.ConsumeCount(accepted);
            _totalCapturedCount += accepted;
            return true;
        }

        private void PushToGridCargoContainers()
        {
            if (Grid == null || container == null || container.IsEmpty()) return;
            var cargoList = GridItemNetwork.Instance.GetConnectedContainers(Grid);
            if (cargoList == null || cargoList.Count == 0) return;

            for (int s = 0; s < container.Size; s++)
            {
                var st = container.GetSlot(s);
                if (st == null || st.IsEmpty || st.item == null) continue;

                for (int c = 0; c < cargoList.Count; c++)
                {
                    var destCargo = cargoList[c];
                    if (destCargo == null || !destCargo.Enabled || destCargo.container == null) continue;
                    if (!destCargo.CanAcceptMass(st.item, 1)) continue;

                    int toMove = Mathf.Min(st.count, 16);
                    var attempt = st.Clone();
                    attempt.count = toMove;
                    var leftover = destCargo.container.Insert(attempt);
                    int moved = toMove - (leftover?.count ?? 0);
                    if (moved <= 0) continue;

                    st.count -= moved;
                    container.SetSlot(s, st.count > 0 ? st : new ItemStack());
                    return;
                }
            }
        }

        // ── Persistence (IMachineProcessState) ──────────────────────────────────
        public void CaptureProcessState(MachineProcessState state)
        {
            if (state == null) return;
            state.SetExtra("autoPushToGrid", autoPushToGridCargo ? 1f : 0f);
            state.SetExtra("totalCaptured", _totalCapturedCount);
        }

        public void RestoreProcessState(MachineProcessState state)
        {
            if (state == null) return;
            autoPushToGridCargo = state.GetExtra("autoPushToGrid", 1f) >= 0.5f;
            _totalCapturedCount = Mathf.Max(0, Mathf.RoundToInt(state.GetExtra("totalCaptured", 0f)));
        }

        // ── IGridDataProvider ───────────────────────────────────────────────────
        public string SourceName => StoreLabel;
        public string DataCategory => "Logistics";

        public string GetDisplayData()
        {
            EnsureContainers();
            return $"RECEIVING CHUTE\n{(Enabled ? "CATCHING" : "DISABLED")}\n{Fill01 * 100f:0}% full\nCaptured: {_totalCapturedCount:N0}";
        }

        // ── Procedural Wide Hopper Visuals & Trigger ────────────────────────────
        private void EnsureCaptureTrigger()
        {
            if (transform.Find("Generated_CaptureTrigger") != null) return;
            var go = new GameObject("Generated_CaptureTrigger");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = captureCenterOffset;
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = captureHalfExtents * 2f;
        }

        public void EnsureVisuals()
        {
            if (transform.Find("Generated_GridChuteVisuals") != null) return;

            var rootMr = GetComponent<MeshRenderer>();
            if (rootMr != null) rootMr.enabled = false;

            var root = new GameObject("Generated_GridChuteVisuals").transform;
            root.SetParent(transform, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            Color baseSteel = new Color(0.20f, 0.23f, 0.27f);
            Color hopperSteel = new Color(0.28f, 0.32f, 0.37f);
            Color throatDark = new Color(0.09f, 0.10f, 0.12f);
            Color hazardYellow = new Color(0.94f, 0.70f, 0.14f);
            Color accentCyan = new Color(0.18f, 0.78f, 0.92f);

            // Base mounting block (fits cleanly on the grid cell while the flared funnel widens above)
            MakeBox(root, "MountBase", new Vector3(0f, -0.26f, 0f), new Vector3(0.96f, 0.46f, 0.96f), Vector3.zero, baseSteel);
            MakeBox(root, "ThroatCenter", new Vector3(0f, -0.02f, 0f), new Vector3(0.76f, 0.08f, 0.76f), Vector3.zero, throatDark);

            // Wide Flared Hopper Wings (flares out to 1.85x cell width so it's a big, easy-to-hit target)
            MakeBox(root, "Slope_North", new Vector3(0f, 0.22f, 0.62f), new Vector3(1.76f, 0.62f, 0.08f), new Vector3(-32f, 0f, 0f), hopperSteel);
            MakeBox(root, "Slope_South", new Vector3(0f, 0.22f, -0.62f), new Vector3(1.76f, 0.62f, 0.08f), new Vector3(32f, 0f, 0f), hopperSteel);
            MakeBox(root, "Slope_East", new Vector3(0.62f, 0.22f, 0f), new Vector3(0.08f, 0.62f, 1.76f), new Vector3(0f, 0f, 32f), hopperSteel);
            MakeBox(root, "Slope_West", new Vector3(-0.62f, 0.22f, 0f), new Vector3(0.08f, 0.62f, 1.76f), new Vector3(0f, 0f, -32f), hopperSteel);

            // Top Hazard Rim & Guidance Strips
            MakeBox(root, "Rim_North", new Vector3(0f, 0.50f, 0.84f), new Vector3(1.82f, 0.08f, 0.10f), Vector3.zero, hazardYellow);
            MakeBox(root, "Rim_South", new Vector3(0f, 0.50f, -0.84f), new Vector3(1.82f, 0.08f, 0.10f), Vector3.zero, hazardYellow);
            MakeBox(root, "Rim_East", new Vector3(0.84f, 0.50f, 0f), new Vector3(0.10f, 0.08f, 1.66f), Vector3.zero, hazardYellow);
            MakeBox(root, "Rim_West", new Vector3(-0.84f, 0.50f, 0f), new Vector3(0.10f, 0.08f, 1.66f), Vector3.zero, hazardYellow);

            MakeBox(root, "Guide_N", new Vector3(0f, 0.55f, 0.84f), new Vector3(1.05f, 0.03f, 0.04f), Vector3.zero, accentCyan);
            MakeBox(root, "Guide_S", new Vector3(0f, 0.55f, -0.84f), new Vector3(1.05f, 0.03f, 0.04f), Vector3.zero, accentCyan);
            MakeBox(root, "Guide_E", new Vector3(0.84f, 0.55f, 0f), new Vector3(0.04f, 0.03f, 1.05f), Vector3.zero, accentCyan);
            MakeBox(root, "Guide_W", new Vector3(-0.84f, 0.55f, 0f), new Vector3(0.04f, 0.03f, 1.05f), Vector3.zero, accentCyan);
        }

        private static void MakeBox(Transform parent, string name, Vector3 pos, Vector3 scale, Vector3 euler, Color color)
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
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader != null && mr.sharedMaterial == null)
                    mr.sharedMaterial = new Material(shader);
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor(BaseColorId, color);
                mpb.SetColor(ColorId, color);
                mr.SetPropertyBlock(mpb);
            }
        }
    }
}
