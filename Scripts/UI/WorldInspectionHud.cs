// Assets/Scripts/VoxelEngine/UI/WorldInspectionHud.cs

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building;
using VoxelEngine.Building.Tiered;
using VoxelEngine.Combat;
using VoxelEngine.Core;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;
using VoxelEngine.Materials;
using VoxelEngine.Power;
using VoxelEngine.Simulation;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    /// <summary>
    /// Premium top-left context overlay describing the block, machine, creature, or
    /// item currently under the crosshair. The overlay is informational only and
    /// never captures pointer input.
    /// </summary>
    public static class WorldInspectionHud
    {
        private const float ProbeInterval = 0.10f;
        // Match the modern 16 m build reach so what you can build at, you can read at.
        private const float ProbeDistance = 16f;

        private static VisualElement _uiRoot;
        private static VisualElement _card;
        private static Label _title;
        private static Label _detail;
        private static Label _status;
        private static VisualElement _healthRow;
        private static VisualElement _healthFill;
        private static Label _healthLabel;
        private static float _nextProbe;
        private static string _lastSignature;
        private static ItemStack _hoveredItem;
        private static bool _visible;

        private struct TargetInfo
        {
            public string title;
            public string detail;
            public string status;
            public float health01;
            public string healthText;
            public bool showHealth;
        }

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (uiRoot == null) return;
            if (_uiRoot == uiRoot && _card != null && _card.parent == uiRoot) return;
            _uiRoot = uiRoot;
            if (_card != null) _card.RemoveFromHierarchy();

            _card = new VisualElement { name = "WorldInspectionHud" };
            _card.style.position = Position.Absolute;
            _card.style.left = 16;
            _card.style.top = 18;
            _card.style.width = 310;
            _card.style.paddingLeft = 14;
            _card.style.paddingRight = 14;
            _card.style.paddingTop = 11;
            _card.style.paddingBottom = 11;
            _card.style.backgroundColor = new StyleColor(new Color(0.025f, 0.035f, 0.055f, 0.94f));
            _card.style.opacity = 0f;
            _card.style.translate = new StyleTranslate(new Translate(new Length(-10f, LengthUnit.Pixel), new Length(0f, LengthUnit.Pixel), 0f));
            _card.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { "opacity", "translate" };
            _card.style.transitionDuration = new System.Collections.Generic.List<TimeValue>
            {
                new(0.13f, TimeUnit.Second),
                new(0.13f, TimeUnit.Second)
            };
            _card.pickingMode = PickingMode.Ignore;
            T.Radius(_card, 8f);
            T.Border(_card, 1f, new Color(T.BorderBright.r, T.BorderBright.g, T.BorderBright.b, 0.78f));
            uiRoot.Add(_card);

            var headingRow = new VisualElement();
            headingRow.style.flexDirection = FlexDirection.Row;
            headingRow.style.alignItems = Align.Center;
            headingRow.pickingMode = PickingMode.Ignore;
            _card.Add(headingRow);

            var accent = new VisualElement();
            accent.style.width = 3;
            accent.style.height = 34;
            accent.style.marginRight = 10;
            accent.style.backgroundColor = new StyleColor(T.AccentCyan);
            accent.pickingMode = PickingMode.Ignore;
            T.Radius(accent, 2f);
            headingRow.Add(accent);

            var textColumn = new VisualElement();
            textColumn.style.flexGrow = 1;
            textColumn.pickingMode = PickingMode.Ignore;
            headingRow.Add(textColumn);

            _title = new Label("TARGET");
            _title.style.fontSize = 13;
            _title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _title.style.letterSpacing = 0.5f;
            _title.style.color = new StyleColor(T.TextPrimary);
            _title.pickingMode = PickingMode.Ignore;
            textColumn.Add(_title);

            _detail = new Label();
            _detail.style.fontSize = 9;
            _detail.style.marginTop = 2;
            _detail.style.letterSpacing = 0.6f;
            _detail.style.color = new StyleColor(T.TextMuted);
            _detail.pickingMode = PickingMode.Ignore;
            textColumn.Add(_detail);

            _status = new Label();
            _status.style.fontSize = 10;
            _status.style.marginTop = 8;
            _status.style.color = new StyleColor(T.TextSecondary);
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.pickingMode = PickingMode.Ignore;
            _card.Add(_status);

            _healthRow = new VisualElement();
            _healthRow.style.marginTop = 8;
            _healthRow.pickingMode = PickingMode.Ignore;
            _card.Add(_healthRow);

            var healthHeader = new VisualElement();
            healthHeader.style.flexDirection = FlexDirection.Row;
            healthHeader.pickingMode = PickingMode.Ignore;
            _healthRow.Add(healthHeader);

            var healthCaption = new Label("INTEGRITY");
            healthCaption.style.flexGrow = 1;
            healthCaption.style.fontSize = 8;
            healthCaption.style.letterSpacing = 1f;
            healthCaption.style.color = new StyleColor(T.TextMuted);
            healthCaption.pickingMode = PickingMode.Ignore;
            healthHeader.Add(healthCaption);

            _healthLabel = new Label();
            _healthLabel.style.fontSize = 8;
            _healthLabel.style.color = new StyleColor(T.TextSecondary);
            _healthLabel.pickingMode = PickingMode.Ignore;
            healthHeader.Add(_healthLabel);

            var healthTrack = new VisualElement();
            healthTrack.style.height = 5;
            healthTrack.style.marginTop = 4;
            healthTrack.style.backgroundColor = new StyleColor(T.BgSlot);
            healthTrack.pickingMode = PickingMode.Ignore;
            T.Radius(healthTrack, 3f);
            _healthRow.Add(healthTrack);

            _healthFill = new VisualElement();
            _healthFill.style.height = Length.Percent(100);
            _healthFill.style.backgroundColor = new StyleColor(T.AccentGreen);
            _healthFill.pickingMode = PickingMode.Ignore;
            T.Radius(_healthFill, 3f);
            healthTrack.Add(_healthFill);
        }

        public static void BindInventoryItem(VisualElement element, ItemStack stack)
        {
            if (element == null || stack == null || stack.IsEmpty || stack.item == null) return;
            var snapshot = stack.Clone();
            void Activate()
            {
                _hoveredItem = snapshot;
                ShowInventoryItem(snapshot);
            }
            element.RegisterCallback<PointerEnterEvent>(_ => Activate());
            element.RegisterCallback<PointerMoveEvent>(_ => Activate());
            element.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (_hoveredItem == snapshot) _hoveredItem = null;
                Hide();
            });
        }

        public static void ClearInventoryHover()
        {
            _hoveredItem = null;
        }

        public static void Tick()
        {
            if (_card == null || _card.parent == null) return;
            if (_hoveredItem != null && !_hoveredItem.IsEmpty)
            {
                ShowInventoryItem(_hoveredItem);
                return;
            }
            if (UIState.IsBlocking)
            {
                Hide();
                return;
            }

            if (Time.unscaledTime < _nextProbe) return;
            _nextProbe = Time.unscaledTime + ProbeInterval;

            var camera = Camera.main;
            if (camera == null)
            {
                Hide();
                return;
            }

            Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            // Probe along the WHOLE ray and take the first hit that resolves into
            // displayable info. Don't give up at the first collider: ghosts, held-tool
            // viewmodels and other transient blockers must never swallow the card when
            // the player has items or tools in hand.
            if (!TryResolveAlongRay(ray, out var info) && !TryResolveVoxelAlongRay(ray, out info))
            {
                Hide();
                return;
            }
            Show(info);
        }

        /// <summary>Walks every hit along the ray (closest first), skipping player
        /// children and known transient rigs, and resolves the FIRST informative one.</summary>
        private static bool TryResolveAlongRay(Ray ray, out TargetInfo info)
        {
            var hits = Physics.RaycastAll(ray, ProbeDistance, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            var localPlayer = Camera.main != null ? Camera.main.GetComponentInParent<VoxelEngine.Player.PlayerController>() : Object.FindAnyObjectByType<VoxelEngine.Player.PlayerController>();
            Transform playerRoot = localPlayer != null ? localPlayer.transform : null;

            for (int i = 0; i < hits.Length; i++)
            {
                var candidate = hits[i];
                if (candidate.collider == null) continue;
                if (playerRoot != null && candidate.collider.transform.IsChildOf(playerRoot)) continue;
                if (localPlayer != null && VoxelEngine.Player.PlayerRaycastFilter.IsOwnPlayerCollider(candidate.collider, localPlayer.transform)) continue;
                if (IsRuntimeSystemCollider(candidate.collider)) continue;
                // Skip transient rigs: build ghosts, viewmodels, held items.
                string rootName = candidate.collider.transform.root.name;
                if (IsTransientRigName(rootName)) continue;

                if (TryResolve(candidate, out info)) return true;
            }
            info = default;
            return false;
        }

        /// <summary>Never present bootstrap/LOD/helper colliders as inspectable world blocks.</summary>
        private static bool IsRuntimeSystemCollider(Collider collider)
        {
            if (collider == null) return true;
            string ownName = collider.gameObject.name;
            string rootName = collider.transform.root != null ? collider.transform.root.name : string.Empty;
            return ownName.IndexOf("Bootstrap", System.StringComparison.OrdinalIgnoreCase) >= 0
                || (collider.transform == collider.transform.root
                    && rootName.IndexOf("Bootstrap", System.StringComparison.OrdinalIgnoreCase) >= 0)
                || ownName.IndexOf("OceanLOD", System.StringComparison.OrdinalIgnoreCase) >= 0
                || ownName.IndexOf("PlanetLOD", System.StringComparison.OrdinalIgnoreCase) >= 0
                || ownName.IndexOf("NativeSphericalWater", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsTransientRigName(string rootName)
        {
            if (string.IsNullOrEmpty(rootName)) return false;
            return rootName.StartsWith("GridGhost", System.StringComparison.Ordinal)
                || rootName.StartsWith("BuildGhost", System.StringComparison.Ordinal)
                || rootName.StartsWith("Viewmodel", System.StringComparison.Ordinal)
                || rootName.StartsWith("PipePrecisionLatticePreview", System.StringComparison.Ordinal)
                || rootName.StartsWith("LedStretchGhost", System.StringComparison.Ordinal);
        }

        private static bool TryResolve(RaycastHit hit, out TargetInfo info)
        {
            info = default;
            if (hit.collider == null) return false;

            // Automated defense network — ammo, filter, auto/manual, HP.
            var defense = hit.collider.GetComponentInParent<Damageable>();
            if (defense != null && DefenseStatus.TryDescribe(defense, out var dInfo))
            {
                info.title = dInfo.title;
                info.detail = dInfo.detail;
                info.status = dInfo.isEmpty
                    ? $"EMPTY · {dInfo.status}"
                    : (dInfo.isLow ? $"LOW · {dInfo.status}" : dInfo.status);
                info.showHealth = dInfo.showHealth;
                info.health01 = dInfo.health01;
                info.healthText = dInfo.healthText;
                return true;
            }

            var conveyor = hit.collider.GetComponentInParent<ConveyorBelt>();
            if (conveyor != null)
            {
                var placed = conveyor.GetComponentInParent<PlacedBlock>();
                info.title = placed?.Item != null ? placed.Item.displayName : $"{conveyor.speed} Conveyor";
                info.detail = $"{conveyor.speed} · {conveyor.shape}".ToUpperInvariant();
                info.status = $"Items in transit: {conveyor.Items.Count}/{Mathf.Max(1, conveyor.maxItems)}";
                ApplyPlacedHealth(placed, ref info);
                return true;
            }

            var chute = hit.collider.GetComponentInParent<ConveyorChute>();
            if (chute != null)
            {
                var placed = chute.GetComponentInParent<PlacedBlock>();
                info.title = placed?.Item != null ? placed.Item.displayName : "Conveyor Chute";
                info.detail = $"CHUTE · {chute.shape}";
                info.status = $"Items descending: {chute.Items.Count}/{Mathf.Max(1, chute.maxItems)}";
                ApplyPlacedHealth(placed, ref info);
                return true;
            }

            var funnel = hit.collider.GetComponentInParent<Funnel>();
            if (funnel != null)
            {
                var placed = funnel.GetComponentInParent<PlacedBlock>();
                info.title = placed?.Item != null ? placed.Item.displayName : "Funnel";
                info.detail = $"FUNNEL · {funnel.Mode}".ToUpperInvariant();
                info.status = $"Buffered: {funnel.BufferedCount}/{Mathf.Max(1, funnel.bufferSize)}";
                ApplyPlacedHealth(placed, ref info);
                return true;
            }

            var splitter = hit.collider.GetComponentInParent<ConveyorSplitter>();
            if (splitter != null)
            {
                var placed = splitter.GetComponentInParent<PlacedBlock>();
                info.title = placed?.Item != null ? placed.Item.displayName : $"Conveyor Splitter {splitter.tier}";
                info.detail = $"SPLITTER · {splitter.tier} · {splitter.RoutingMode}".ToUpperInvariant();
                info.status = $"Buffered: {splitter.BufferedCount}/{Mathf.Max(1, splitter.bufferSize)} · Outputs: {splitter.ConnectedOutputCount}/{Mathf.Max(1, splitter.OutputCount)}";
                ApplyPlacedHealth(placed, ref info);
                return true;
            }

            var gridBlock = hit.collider.GetComponentInParent<GridBlock>();
            if (gridBlock != null)
            {
                info.title = string.IsNullOrWhiteSpace(gridBlock.blockName) ? gridBlock.name : gridBlock.blockName;
                info.detail = "GRID BLOCK";
                info.status = DescribePower(gridBlock.gameObject);
                var gpf = gridBlock.GetComponent<BlockPaint>();
                if (gpf != null && gpf.Finish != PaintFinishId.None)
                    info.status = string.IsNullOrEmpty(info.status)
                        ? $"Finish: {PaintFinishCatalog.DisplayName(gpf.Finish)}"
                        : $"{info.status} · {PaintFinishCatalog.DisplayName(gpf.Finish)}";
                info.showHealth = gridBlock.maxHP > 0f;
                info.health01 = gridBlock.maxHP > 0f ? Mathf.Clamp01(gridBlock.currentHP / gridBlock.maxHP) : 0f;
                info.healthText = $"{Mathf.Max(0f, gridBlock.currentHP):0}/{gridBlock.maxHP:0}";
                return true;
            }

            var tiered = hit.collider.GetComponentInParent<PlacedTieredBlock>();
            if (tiered != null && tiered.definition != null)
            {
                int maximum = tiered.definition.GetStats(tiered.tier).hp;
                info.title = tiered.definition.displayName;
                info.detail = $"{tiered.tier} BUILDING".ToUpperInvariant();
                info.status = "Building Hammer construction piece";
                info.showHealth = maximum > 0;
                info.health01 = maximum > 0 ? Mathf.Clamp01(tiered.hp / (float)maximum) : 0f;
                info.healthText = $"{Mathf.Max(0, tiered.hp)}/{maximum}";
                return true;
            }

            var placedBlock = hit.collider.GetComponentInParent<PlacedBlock>();
            if (placedBlock != null && placedBlock.Item != null)
            {
                info.title = placedBlock.Item.displayName;
                info.detail = string.IsNullOrWhiteSpace(placedBlock.Item.category)
                    ? "PLACED BLOCK"
                    : placedBlock.Item.category.ToUpperInvariant();
                info.status = DescribePower(placedBlock.gameObject);
                var pf = placedBlock.GetComponent<BlockPaint>();
                if (pf != null && pf.Finish != PaintFinishId.None)
                    info.status = string.IsNullOrEmpty(info.status)
                        ? $"Finish: {PaintFinishCatalog.DisplayName(pf.Finish)}"
                        : $"{info.status} · {PaintFinishCatalog.DisplayName(pf.Finish)}";
                ApplyPlacedHealth(placedBlock, ref info);
                return true;
            }

            var dropped = hit.collider.GetComponentInParent<DroppedItem>();
            if (dropped != null && dropped.stack != null && !dropped.stack.IsEmpty)
            {
                info.title = dropped.stack.item.displayName;
                info.detail = "DROPPED ITEM";
                info.status = $"Stack: {dropped.stack.count}";
                return true;
            }

            var behaviours = hit.collider.GetComponentsInParent<MonoBehaviour>(true);
            foreach (var behaviour in behaviours)
            {
                if (behaviour is IMachine machine)
                {
                    info.title = machine.MachineName;
                    info.detail = "MACHINE";
                    info.status = machine.IsOnline
                        ? $"{(machine.IsActive ? "Running" : "Idle")} · {machine.CurrentWattage:0} W"
                        : "Offline · no power";
                    return true;
                }
            }

            var world = ActiveWorld.Current;
            if (world != null)
            {
                Vector3 samplePoint = hit.point - hit.normal.normalized * 0.15f;
                if (TryDescribeVoxel(world, world.WorldToVoxel(samplePoint), out info)) return true;
            }

            // A terrain chunk without a resolved voxel should not become a meaningless root
            // object name; let the ray-marched voxel fallback below identify the real material.
            if (hit.collider.gameObject.name.StartsWith("Chunk_", System.StringComparison.Ordinal))
                return false;

            string fallback = hit.collider.transform.root.name;
            if (string.IsNullOrWhiteSpace(fallback)) return false;
            info.title = fallback.Replace("(Clone)", string.Empty).Trim();
            info.detail = "WORLD OBJECT";
            info.status = $"Distance: {hit.distance:0.0} m";
            return true;
        }

        private static bool TryResolveVoxelAlongRay(Ray ray, out TargetInfo info)
        {
            info = default;
            var world = ActiveWorld.Current;
            if (world == null) return false;

            Vector3Int lastVoxel = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
            // A collider can be deferred for a newly streamed chunk while its voxel data is
            // already valid. Marching the short inspection reach keeps the top-left title alive
            // during that hand-off instead of flashing blank/no-object.
            for (float distance = 0.35f; distance <= ProbeDistance; distance += 0.5f)
            {
                Vector3Int voxelPosition = world.WorldToVoxel(ray.GetPoint(distance));
                if (voxelPosition == lastVoxel) continue;
                lastVoxel = voxelPosition;
                if (TryDescribeVoxel(world, voxelPosition, out info)) return true;
            }
            return false;
        }

        private static bool TryDescribeVoxel(IVoxelWorld world, Vector3Int voxelPosition, out TargetInfo info)
        {
            info = default;
            if (world == null) return false;
            Voxel voxel;
            if (world is VoxelEngine.Cosmos.SphereWorld sphere)
            {
                if (!sphere.TryGetVoxelReady(voxelPosition, out voxel)) return false;
            }
            else
            {
                voxel = world.GetVoxelWorld(voxelPosition);
            }
            if (voxel.density <= 0 && voxel.material == (byte)MaterialId.Air) return false;

            byte materialByte = voxel.material == (byte)MaterialId.LegacySolidFloor
                ? (byte)MaterialId.Stone
                : voxel.material;
            var definition = world.MaterialRegistry?.Get(materialByte);
            var materialId = (MaterialId)materialByte;
            info.title = definition != null && !string.IsNullOrWhiteSpace(definition.displayName)
                ? definition.displayName
                : materialId.ToString();
            info.detail = "VOXEL MATERIAL";
            info.status = definition != null
                ? $"Hardness: {definition.hardness:0.0} · Mining tier: {definition.miningTier}"
                : $"Material ID: {(byte)materialId}";
            return true;
        }

        private static void ShowInventoryItem(ItemStack stack)
        {
            if (stack == null || stack.IsEmpty || stack.item == null) return;
            string detail = string.IsNullOrWhiteSpace(stack.item.category)
                ? "INVENTORY ITEM"
                : stack.item.category.ToUpperInvariant();
            string status = $"Stack: {stack.count}/{Mathf.Max(1, ItemStack.MaxItemsPerStack(stack.item))} · Mass: {stack.item.massPerUnit * stack.count:0.##}";
            if (stack.item is ToolItem tool && tool.maxDurability > 0)
                status += $" · Durability: {Mathf.Max(0, stack.durability)}/{tool.maxDurability}";
            Show(new TargetInfo
            {
                title = stack.item.displayName,
                detail = detail,
                status = status,
                showHealth = false
            });
        }

        private static void ApplyPlacedHealth(PlacedBlock placed, ref TargetInfo info)
        {
            if (placed?.Item == null || placed.Item.blockHealth <= 0) return;
            info.showHealth = true;
            info.health01 = Mathf.Clamp01(placed.Hp / (float)placed.Item.blockHealth);
            info.healthText = $"{Mathf.Max(0, placed.Hp)}/{placed.Item.blockHealth}";
        }

        private static string DescribePower(GameObject target)
        {
            if (target == null) return string.Empty;
            var consumer = target.GetComponentInChildren<PowerConsumer>(true);
            if (consumer != null)
                return consumer.IsPowered
                    ? $"Powered · {consumer.wattsPerSecond:0} W demand"
                    : $"Unpowered · {consumer.wattsPerSecond:0} W demand";
            var generator = target.GetComponentInChildren<PowerGenerator>(true);
            if (generator != null)
                return generator.isOn
                    ? $"Generating · {generator.wattsPerSecond:0} W"
                    : "Generator offline";
            return string.Empty;
        }

        private static void Show(TargetInfo info)
        {
            string signature = $"{info.title}|{info.detail}|{info.status}|{info.healthText}";
            if (signature != _lastSignature)
            {
                _lastSignature = signature;
                _title.text = info.title;
                _detail.text = info.detail;
                _status.text = info.status;
                _status.style.display = string.IsNullOrWhiteSpace(info.status) ? DisplayStyle.None : DisplayStyle.Flex;
                _healthRow.style.display = info.showHealth ? DisplayStyle.Flex : DisplayStyle.None;
                if (info.showHealth)
                {
                    _healthFill.style.width = new StyleLength(new Length(info.health01 * 100f, LengthUnit.Percent));
                    _healthFill.style.backgroundColor = new StyleColor(Color.Lerp(T.AccentRed, T.AccentGreen, info.health01));
                    _healthLabel.text = info.healthText;
                }
            }

            if (_visible) return;
            _visible = true;
            _card.style.opacity = 1f;
            _card.style.translate = new StyleTranslate(new Translate(new Length(0f, LengthUnit.Pixel), new Length(0f, LengthUnit.Pixel), 0f));
        }

        public static void Hide()
        {
            if (_card == null || !_visible) return;
            _visible = false;
            _lastSignature = null;
            _card.style.opacity = 0f;
            _card.style.translate = new StyleTranslate(new Translate(new Length(-10f, LengthUnit.Pixel), new Length(0f, LengthUnit.Pixel), 0f));
        }
    }
}
