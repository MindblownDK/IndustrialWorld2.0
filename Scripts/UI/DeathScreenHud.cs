// Assets/Scripts/VoxelEngine/UI/DeathScreenHud.cs
//
// Premium full-screen death/respawn overlay. It lists the safe respawn anchors
// currently available: world spawn, the active linked spawn, and live beds / cryobeds.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building;
using VoxelEngine.GridSystem;
using VoxelEngine.Player;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class DeathScreenHud
    {
        private static VisualElement _root;
        private static VisualElement _overlay;
        private static PlayerStats _deadPlayer;
        private static bool _visible;
        private static bool _blocking;

        private struct RespawnChoice
        {
            public string title;
            public string detail;
            public Vector3 position;
            public Color accent;
            /// <summary>World-spawn choices route through PlayerSpawner.Respawn() —
            /// the body-anchored, self-healing path (raw scene points go stale).</summary>
            public bool isWorldSpawn;
            /// <summary>14.60.5 - frame-independent bed: route through the cosmic
            /// respawn (re-anchors origin + streaming) instead of the scene point.</summary>
            public bool isCosmic;
            public double cosmicXKm, cosmicYKm, cosmicZKm;
        }

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _overlay != null && _overlay.parent == uiRoot) return;
            _root = uiRoot;
            if (_overlay != null) _overlay.RemoveFromHierarchy();

            _overlay = new VisualElement { name = "DeathScreenHud" };
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0;
            _overlay.style.right = 0;
            _overlay.style.top = 0;
            _overlay.style.bottom = 0;
            _overlay.style.backgroundColor = new StyleColor(new Color(0.01f, 0.012f, 0.018f, 0.92f));
            _overlay.style.display = DisplayStyle.None;
            _overlay.pickingMode = PickingMode.Position;
            uiRoot.Add(_overlay);

            if (_visible) Rebuild();
        }

        public static void Show(PlayerStats player)
        {
            _deadPlayer = player;
            _visible = true;
            if (!_blocking)
            {
                UIState.PushBlock();
                UIState.PushHardPause();   // you're dead — freeze player control (world keeps its own time)
                _blocking = true;
            }
            if (_overlay != null) Rebuild();
        }

        public static void Hide()
        {
            _visible = false;
            _deadPlayer = null;
            PlayerStats.ClearDeathCause();
            if (_overlay != null) _overlay.style.display = DisplayStyle.None;
            if (_blocking)
            {
                UIState.PopHardPause();
                UIState.PopBlock();
                _blocking = false;
            }
        }

        private static void Rebuild()
        {
            if (_overlay == null) return;
            _overlay.Clear();
            _overlay.style.display = DisplayStyle.Flex;
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;

            var panel = new VisualElement();
            panel.style.width = 560;
            panel.style.maxWidth = new StyleLength(new Length(82f, LengthUnit.Percent));
            panel.style.paddingLeft = 26;
            panel.style.paddingRight = 26;
            panel.style.paddingTop = 24;
            panel.style.paddingBottom = 24;
            panel.style.backgroundColor = new StyleColor(new Color(0.035f, 0.040f, 0.055f, 0.96f));
            T.Radius(panel, 18f);
            T.Border(panel, 1, new Color(0.85f, 0.18f, 0.14f, 0.55f));
            _overlay.Add(panel);

            var title = new Label("CRUSADER DOWN");
            title.style.fontSize = 28;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 2.0f;
            title.style.color = new Color(1.0f, 0.30f, 0.22f);
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            panel.Add(title);

            // Hazard death causes (solar, singularity, quasar jets) get their own line.
            if (!string.IsNullOrEmpty(PlayerStats.LastDeathCause))
            {
                var cause = new Label(PlayerStats.LastDeathCause);
                cause.style.marginTop = 3;
                cause.style.fontSize = 13;
                cause.style.letterSpacing = 1.4f;
                cause.style.color = new Color(0.95f, 0.62f, 0.40f);
                cause.style.unityTextAlign = TextAnchor.MiddleCenter;
                panel.Add(cause);
            }

            var subtitle = new Label("Select a respawn anchor");
            subtitle.style.marginTop = 4;
            subtitle.style.marginBottom = 18;
            subtitle.style.fontSize = 12;
            subtitle.style.letterSpacing = 1.0f;
            subtitle.style.color = new Color(0.72f, 0.78f, 0.86f);
            subtitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            panel.Add(subtitle);

            var choices = GatherRespawnChoices();
            for (int i = 0; i < choices.Count; i++)
                panel.Add(BuildChoiceButton(choices[i]));
        }

        private static VisualElement BuildChoiceButton(RespawnChoice choice)
        {
            var btn = new Button(() => Respawn(choice));
            btn.style.marginBottom = 8;
            btn.style.minHeight = 58;
            btn.style.paddingLeft = 14;
            btn.style.paddingRight = 14;
            btn.style.paddingTop = 8;
            btn.style.paddingBottom = 8;
            btn.style.backgroundColor = new StyleColor(new Color(0.075f, 0.085f, 0.11f, 0.98f));
            T.Radius(btn, 10f);
            T.Border(btn, 1, new Color(choice.accent.r, choice.accent.g, choice.accent.b, 0.45f));

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Column;
            row.pickingMode = PickingMode.Ignore;
            btn.Add(row);

            var title = new Label(choice.title);
            title.style.fontSize = 13;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = choice.accent;
            title.pickingMode = PickingMode.Ignore;
            row.Add(title);

            var detail = new Label(choice.detail);
            detail.style.marginTop = 2;
            detail.style.fontSize = 10;
            detail.style.color = new Color(0.70f, 0.76f, 0.84f);
            detail.pickingMode = PickingMode.Ignore;
            row.Add(detail);
            return btn;
        }

        private static void Respawn(RespawnChoice choice)
        {
            var player = _deadPlayer != null ? _deadPlayer : PlayerStats.Instance;
            Hide();
            if (player == null) return;

            if (choice.isWorldSpawn)
            {
                // Healing path WITH stat restore (9.5.4: going straight to the spawner
                // skipped the revive — players respawned at 0 health).
                player.RespawnAtWorldSpawn();
                return;
            }
            if (choice.isCosmic)
            {
                player.RespawnAtCosmic(choice.cosmicXKm, choice.cosmicYKm, choice.cosmicZKm);
                return;
            }
            player.RespawnAt(choice.position);
        }

        private static List<RespawnChoice> GatherRespawnChoices()
        {
            var list = new List<RespawnChoice>(8);
            var session = VoxelEngine.Menu.WorldSession.Instance;

            // World spawn: prefer the initialized true spawn, but tolerate non-zero legacy saves.
            // The 0,250,0 fallback is only used if absolutely nothing else exists — PlayerSpawner
            // now initializes worldSpawnPoint to the real grounded position on first play.
            Vector3 worldSpawn = new Vector3(0f, 250f, 0f);
            if (session != null && !session.TryResolveWorldSpawn(out worldSpawn))
                worldSpawn = session.worldSpawnPoint;

            AddUnique(list, new RespawnChoice
            {
                title = "World Spawn",
                detail = FormatPosition(worldSpawn),
                position = worldSpawn,
                accent = new Color(0.42f, 0.75f, 1.0f),
                isWorldSpawn = true
            });

            // 14.38.0 - a linked spawn whose structure is GONE (bed demolished
            // while the player was offline, cryobed ground to scrap) is healed
            // here rather than offered: the stale point is cleared and the
            // player falls back to the honest choices below.
            // 14.60.5 - the scene-object sanity checks (does the bed still exist?
            // is the cryobed out of O2?) scan colliders around the stored SCENE
            // point. That point is only meaningful when the bed is on the planet
            // currently streamed here - for a bed on another world the scan found
            // nothing and CLEARED a perfectly healthy link. Those checks now run
            // only for same-world beds; cross-world beds ride the cosmic record.
            bool bedIsLocalWorld = true;
            if (session != null && !string.IsNullOrEmpty(session.bedSpawnBodyName))
            {
                var activeBody = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
                string here = activeBody != null && activeBody.settings != null ? activeBody.settings.bodyName : "";
                bedIsLocalWorld = string.Equals(here, session.bedSpawnBodyName, System.StringComparison.OrdinalIgnoreCase);
            }

            if (session != null && session.hasBedSpawn && bedIsLocalWorld && !LinkedSpawnStillExists(session.bedSpawnPoint))
            {
                session.hasBedSpawn = false;
                session.SaveSpawnSidecar();
                Debug.Log("[DeathScreen] Linked spawn structure no longer exists — cleared.");
            }

            if (session != null && session.hasBedSpawn
                && (!bedIsLocalWorld || !LinkedSpawnIsUnavailableCryobed(session.bedSpawnPoint)))
            {
                // 14.60.6 - SELF-HEAL: the structure scan just verified the scene
                // point, so a missing/rotted cosmic record (pre-body-relative links,
                // cryobed links that never wrote one) is refreshed from it here -
                // no re-sleeping required.
                if (bedIsLocalWorld &&
                    (string.IsNullOrEmpty(session.bedSpawnBodyName) ||
                     !VoxelEngine.Player.PlayerSpawner.TryResolveBedCosmicKm(session, out _)))
                {
                    session.RefreshBedCosmic();
                    session.SaveSpawnSidecar();
                }

                // 14.60.6 - the cosmic respawn is for CROSS-WORLD beds only; a bed
                // on the planet under your feet uses the verified scene point (the
                // classic path - routing it through the cosmic teleport turned any
                // imperfect record into a random healed surface point).
                Unity.Mathematics.double3 bedKm = default;
                bool cosmicBed = !bedIsLocalWorld &&
                    VoxelEngine.Player.PlayerSpawner.TryResolveBedCosmicKm(session, out bedKm);
                if (bedIsLocalWorld || cosmicBed)
                {
                    // Resolve the actual name of the linked spawn instead of generic "Linked Spawn".
                    string linkedName = bedIsLocalWorld ? ResolveLinkedSpawnName(session.bedSpawnPoint) : "Linked Spawn";
                    string where = bedIsLocalWorld
                        ? FormatPosition(session.bedSpawnPoint)
                        : "on " + session.bedSpawnBodyName;
                    AddUnique(list, new RespawnChoice
                    {
                        title = linkedName,
                        detail = "Linked spawn · " + where,
                        position = session.bedSpawnPoint,
                        accent = new Color(0.30f, 0.95f, 0.62f),
                        isCosmic = cosmicBed,
                        cosmicXKm = bedKm.x, cosmicYKm = bedKm.y, cosmicZKm = bedKm.z
                    });
                }
            }

            string me = VoxelEngine.Networking.NetworkSession.LocalPlayerId ?? "";
            foreach (var bed in Object.FindObjectsByType<Bed>(FindObjectsInactive.Exclude))
            {
                if (bed == null) continue;
                // Beds are personal (14.38.0): only the owner and the owner's
                // team respawn here. Unowned legacy beds welcome anyone.
                if (!bed.UsableBy(me)) continue;
                Vector3 pos = bed.transform.position + Vector3.up * 1.2f;
                AddUnique(list, new RespawnChoice
                {
                    title = string.IsNullOrWhiteSpace(bed.displayName) ? "Bed" : bed.displayName,
                    detail = "Bed · " + FormatPosition(pos),
                    position = pos,
                    accent = new Color(0.95f, 0.72f, 0.25f)
                });
            }

            foreach (var cryo in Object.FindObjectsByType<Cryobed>(FindObjectsInactive.Exclude))
            {
                if (cryo == null || !cryo.claimedByLocalPlayer || !cryo.IsAvailableForRespawn) continue;
                Vector3 pos = cryo.SpawnPoint;
                AddUnique(list, new RespawnChoice
                {
                    title = string.IsNullOrWhiteSpace(cryo.displayName) ? "Cryobed" : cryo.displayName,
                    detail = cryo.AvailabilityText + " · " + FormatPosition(pos),
                    position = pos,
                    accent = new Color(0.45f, 0.85f, 1.0f)
                });
            }

            foreach (var cryo in Object.FindObjectsByType<GridCryobed>(FindObjectsInactive.Exclude))
            {
                if (cryo == null || !cryo.claimedByLocalPlayer || !cryo.IsAvailableForRespawn) continue;
                Vector3 pos = cryo.SpawnPoint;
                AddUnique(list, new RespawnChoice
                {
                    title = string.IsNullOrWhiteSpace(cryo.blockName) ? "Grid Cryobed" : cryo.blockName,
                    detail = cryo.AvailabilityText + " · " + FormatPosition(pos),
                    position = pos,
                    accent = new Color(0.45f, 0.85f, 1.0f)
                });
            }

            return list;
        }

        /// <summary>
        /// Returns the display name of whatever bed/cryobed sits at the linked spawn
        /// position, so the death screen shows "My Awesome Bunker" instead of generic
        /// "Linked Spawn". Falls back to "Linked Spawn" if nothing matches.
        /// </summary>
        private static string ResolveLinkedSpawnName(Vector3 linkedPos)
        {
            const float tolSq = 2.5f; // slightly larger tolerance for floating point
            foreach (var bed in Object.FindObjectsByType<Bed>(FindObjectsInactive.Exclude))
            {
                if (bed == null) continue;
                Vector3 pos = bed.transform.position + Vector3.up * 1.2f;
                if ((pos - linkedPos).sqrMagnitude < tolSq)
                    return string.IsNullOrWhiteSpace(bed.displayName) ? "Bed" : bed.displayName;
            }
            foreach (var cryo in Object.FindObjectsByType<Cryobed>(FindObjectsInactive.Exclude))
            {
                if (cryo == null) continue;
                if ((cryo.SpawnPoint - linkedPos).sqrMagnitude < tolSq)
                    return string.IsNullOrWhiteSpace(cryo.displayName) ? "Cryobed" : cryo.displayName;
            }
            foreach (var cryo in Object.FindObjectsByType<GridCryobed>(FindObjectsInactive.Exclude))
            {
                if (cryo == null) continue;
                if ((cryo.SpawnPoint - linkedPos).sqrMagnitude < tolSq)
                    return string.IsNullOrWhiteSpace(cryo.blockName) ? "Grid Cryobed" : cryo.blockName;
            }
            return "Linked Spawn";
        }

        /// <summary>Does ANY spawn structure (bed, cryobed, grid cryobed) still
        /// stand at the linked point? A demolished bed must not keep offering
        /// its ghost as a respawn (14.38.0).</summary>
        private static bool LinkedSpawnStillExists(Vector3 linkedPos)
        {
            const float tolSq = 2.5f;
            foreach (var bed in Object.FindObjectsByType<Bed>(FindObjectsInactive.Exclude))
                if (bed != null && (bed.transform.position + Vector3.up * 1.2f - linkedPos).sqrMagnitude < tolSq)
                    return true;
            foreach (var cryo in Object.FindObjectsByType<Cryobed>(FindObjectsInactive.Exclude))
                if (cryo != null && (cryo.SpawnPoint - linkedPos).sqrMagnitude < tolSq)
                    return true;
            foreach (var cryo in Object.FindObjectsByType<GridCryobed>(FindObjectsInactive.Exclude))
                if (cryo != null && (cryo.SpawnPoint - linkedPos).sqrMagnitude < tolSq)
                    return true;
            return false;
        }

        private static bool LinkedSpawnIsUnavailableCryobed(Vector3 linkedPos)
        {
            foreach (var cryo in Object.FindObjectsByType<Cryobed>(FindObjectsInactive.Exclude))
                if (cryo != null && (cryo.SpawnPoint - linkedPos).sqrMagnitude < 1.5f)
                    return !cryo.IsAvailableForRespawn;
            foreach (var cryo in Object.FindObjectsByType<GridCryobed>(FindObjectsInactive.Exclude))
                if (cryo != null && (cryo.SpawnPoint - linkedPos).sqrMagnitude < 1.5f)
                    return !cryo.IsAvailableForRespawn;
            return false;
        }

        private static void AddUnique(List<RespawnChoice> list, RespawnChoice choice)
        {
            for (int i = 0; i < list.Count; i++)
                if ((list[i].position - choice.position).sqrMagnitude < 1.0f) return;
            list.Add(choice);
        }

        private static string FormatPosition(Vector3 p)
            => $"{p.x:0}, {p.y:0}, {p.z:0}";
    }
}
