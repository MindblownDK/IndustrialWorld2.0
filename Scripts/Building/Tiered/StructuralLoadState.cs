using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Building.Tiered
{
    /// <summary>Runtime load path for newly placed suspended Floors and Roofs.</summary>
    public sealed class StructuralLoadState : MonoBehaviour
    {
        public BuildFamily loadFamily;
        public int spanFromSupport;
        public Vector3 supportAnchor;
        public bool armed;
        public bool verticalPiece;
        public bool fittingPiece;
        public bool pillarPiece;
        public PlacedTieredBlock hostPiece;
        private float _nextCheck;

        private const float AuditInterval = 0.3f;
        // An unsupported piece no longer vanishes instantly: it visibly loses
        // health for about ten seconds and then collapses, giving the player a
        // window to see it fail and to rebuild the support under it.
        private const float DecaySeconds = 10f;

        public void Arm(int span, Vector3 anchor)
        {
            spanFromSupport = Mathf.Max(1, span);
            supportAnchor = anchor;
            armed = true;
            _nextCheck = Time.time + AuditInterval;
        }

        /// <summary>
        /// Arms a wall-type piece (or a railing) with the lightweight base check:
        /// it survives while something carries its base line and collapses when
        /// that support is destroyed. Its loadFamily is its own family, so span
        /// logic never mistakes it for a deck.
        /// </summary>
        public void ArmVertical(BuildFamily family)
        {
            loadFamily = family;
            verticalPiece = true;
            armed = true;
            _nextCheck = Time.time + AuditInterval;
        }

        /// <summary>
        /// Arms a fitting (Door, Garage Door, Window Pane, Hatch Lid) against the
        /// frame that holds it: the fitting lives exactly as long as its host. A
        /// base probe would be wrong here - a door's base line rests on the floor,
        /// but its life depends on the doorway.
        /// </summary>
        public void ArmFitting(PlacedTieredBlock host, BuildFamily family)
        {
            if (host == null) return; // no recorded frame: stay legacy-stable
            loadFamily = family;
            hostPiece = host;
            fittingPiece = true;
            armed = true;
            _nextCheck = Time.time + AuditInterval;
        }

        /// <summary>
        /// Pings every armed piece near a destroyed block so chain collapses
        /// ripple in tenths of a second instead of one audit interval per link.
        /// </summary>
        public static void NotifySupportRemoved(Vector3 position)
        {
            var hits = Physics.OverlapSphere(position, 9f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                var load = hits[i] != null ? hits[i].GetComponentInParent<StructuralLoadState>() : null;
                if (load != null && load.armed) load.RequestImmediateAudit();
            }
        }

        /// <summary>The destroyed collider is gone next frame; audit right after.</summary>
        public void RequestImmediateAudit()
            => _nextCheck = Mathf.Min(_nextCheck, Time.time + 0.1f);

        /// <summary>
        /// Arms a Pillar: it stands while its chain reaches the ground, its base
        /// rests on solid support, or its top hangs from a live block. A fully
        /// detached pillar decays with the rest of the building.
        /// </summary>
        public void ArmPillar()
        {
            loadFamily = BuildFamily.Pillar;
            pillarPiece = true;
            armed = true;
            _nextCheck = Time.time + AuditInterval;
        }

        private void Update()
        {
            if (!armed || Time.time < _nextCheck) return;
            _nextCheck = Time.time + AuditInterval;
            bool stands = fittingPiece ? hostPiece != null
                : pillarPiece ? HasPillarSupport()
                : verticalPiece ? HasBase()
                : HasLoadPath();
            if (stands) return;
            DecayTick();
        }

        /// <summary>Unsupported: drain health each audit tick, collapse at zero.</summary>
        private void DecayTick()
        {
            var own = GetComponent<PlacedTieredBlock>();
            if (own == null || own.definition == null) { Destroy(gameObject); return; }
            int maximum = Mathf.Max(1, own.definition.GetStats(own.tier).hp);
            own.hp -= Mathf.Max(1, Mathf.CeilToInt(maximum * (AuditInterval / DecaySeconds)));
            if (own.hp <= 0) { Destroy(gameObject); return; }
            VoxelEngine.Thermal.BlockDamageVisual.ReportDamage(own,
                1f - Mathf.Clamp01(own.hp / (float)maximum));
        }

        /// <summary>
        /// A pillar stands while (a) its pillar chain reaches the ground, (b) its
        /// base rests on terrain, a Foundation, a wall line or a live deck - but
        /// never on another hanging pillar, or two detached pillars would hold
        /// each other up forever - or (c) its top hangs from any live block, the
        /// deliberate hanging-chain build.
        /// </summary>
        private bool HasPillarSupport()
        {
            var own = GetComponent<PlacedTieredBlock>();
            if (own == null || own.definition == null) return true;
            if (TryGetComponent<AdjustablePillar>(out var pillar) && pillar.IsSupportGrounded())
                return true;

            // (b) base contact, excluding pillars (the chain check owns those).
            var below = Physics.OverlapBox(transform.position - transform.up * 0.15f,
                new Vector3(0.6f, 0.25f, 0.6f), transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < below.Length; i++)
            {
                Collider collider = below[i];
                if (collider == null || collider.transform.IsChildOf(transform)) continue;
                if (BuildSystemV2.IsDynamicBody(collider)) continue;
                var block = collider.GetComponentInParent<PlacedTieredBlock>();
                if (block == null) return true; // terrain or static world surface
                if (block == own || block.definition == null) continue;
                BuildFamily family = block.definition.family;
                if (block.GetComponent<AdjustablePillar>() != null || family == BuildFamily.Pillar) continue;
                if (family == BuildFamily.Foundation || BuildFamilyInfo.IsDeck(family)) return true;
                if (IsVerticalSupport(family)
                    && (TryResolveSupportBase(block, out var deck) || deck != null))
                    return true;
            }

            // (c) hanging from a live block above.
            float height = pillar != null ? pillar.currentHeight : 5.625f;
            var above = Physics.OverlapBox(transform.position + transform.up * (height + 0.1f),
                new Vector3(0.5f, 0.3f, 0.5f), transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < above.Length; i++)
            {
                Collider collider = above[i];
                if (collider == null || collider.transform.IsChildOf(transform)) continue;
                if (BuildSystemV2.IsDynamicBody(collider)) continue;
                var block = collider.GetComponentInParent<PlacedTieredBlock>();
                if (block == own) continue;
                return true; // any live block or overhanging surface holds the top
            }
            return false;
        }

        /// <summary>
        /// A vertical piece stands while anything carries its base line - or,
        /// for a piece hung below a floor, while a live deck sits on its head
        /// (relayed through stacked hanging walls).
        /// </summary>
        private bool HasBase()
        {
            var own = GetComponent<PlacedTieredBlock>();
            if (own == null || own.definition == null) return true;
            bool grounded = TryResolveSupportBase(own, out var carryingDeck);
            return grounded || carryingDeck != null || HasOverheadCarrier(own, 4);
        }

        /// <summary>
        /// True when a live deck rests against this piece's head, directly or
        /// through further hanging wall pieces. Hanging pieces never GRANT span
        /// or support to anything else - they only keep themselves alive - so
        /// no cantilever or ladder rule changes.
        /// </summary>
        private static bool HasOverheadCarrier(PlacedTieredBlock piece, int depth)
        {
            if (piece == null || piece.definition == null || depth <= 0) return false;
            float height = piece.definition.family == BuildFamily.HalfWall ? 2.8f : 5.625f;
            var above = Physics.OverlapBox(
                piece.transform.position + piece.transform.up * (height + 0.1f),
                new Vector3(0.6f, 0.25f, 0.6f), piece.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < above.Length; i++)
            {
                Collider collider = above[i];
                if (collider == null || collider.transform.IsChildOf(piece.transform)) continue;
                if (BuildSystemV2.IsDynamicBody(collider)) continue;
                var block = collider.GetComponentInParent<PlacedTieredBlock>();
                if (block == null || block == piece || block.definition == null) continue;
                BuildFamily family = block.definition.family;
                if (BuildFamilyInfo.IsDeck(family)) return true;
                if (family != BuildFamily.Pillar && IsVerticalSupport(family)
                    && HasOverheadCarrier(block, depth - 1)) return true;
            }
            return false;
        }

        private bool HasLoadPath()
        {
            var own = GetComponent<PlacedTieredBlock>();
            if (own == null || own.definition == null) return true;
            BuildFamily family = own.definition.family;
            float radius = 8.1f;
            var hits = Physics.OverlapSphere(transform.position, radius, ~0, QueryTriggerInteraction.Ignore);
            var visited = new HashSet<PlacedTieredBlock>();
            var blocks = new List<PlacedTieredBlock>(hits.Length);
            for (int i = 0; i < hits.Length; i++)
            {
                var block = hits[i] != null ? hits[i].GetComponentInParent<PlacedTieredBlock>() : null;
                if (block == null || block == own || block.definition == null || !visited.Add(block)) continue;
                blocks.Add(block);
            }

            // Pass 1: a direct grounded support is adopted and resets the span,
            // so it must win over any deck neighbour that merely relays its own
            // path. The old single pass returned through whichever block the
            // physics query happened to list first, which is why a freshly
            // grounded pillar under a span-two floor often never got adopted.
            // A wall standing on a suspended deck is remembered as a pass-through
            // carrier instead: it relays that deck's span but never resets ours.
            int bestCarrierSpan = int.MaxValue;
            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                BuildFamily other = block.definition.family;
                bool grounded;
                if (block.TryGetComponent<AdjustablePillar>(out var adjustablePillar))
                    grounded = adjustablePillar.IsSupportGrounded();
                else if (IsVerticalSupport(other))
                {
                    grounded = TryResolveSupportBase(block, out var carryingDeck);
                    if (!grounded && carryingDeck != null && carryingDeck.armed
                        && ReachesLevel(block, family))
                        bestCarrierSpan = Mathf.Min(bestCarrierSpan, carryingDeck.spanFromSupport);
                }
                else if (other == BuildFamily.Foundation && !BuildFamilyInfo.IsRoofPanel(family))
                    grounded = true;
                else
                    continue;
                if (grounded && ReachesLevel(block, family))
                {
                    AdoptDirectSupport(block);
                    return true;
                }
            }

            // Pass 2: no direct support in reach - survive through a compatible
            // neighbouring deck (or a wall pass-through) strictly closer to one.
            if (bestCarrierSpan < spanFromSupport) return true;
            for (int i = 0; i < blocks.Count; i++)
            {
                var load = blocks[i].GetComponent<StructuralLoadState>();
                bool compatibleDeck = load != null && (BuildFamilyInfo.IsRoofPanel(loadFamily)
                    ? BuildFamilyInfo.IsRoofPanel(load.loadFamily)
                    : BuildFamilyInfo.IsDeck(load.loadFamily));
                if (compatibleDeck && load.armed && load.spanFromSupport < spanFromSupport)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Resolves what actually carries a wall-type support (Wall, Doorway,
        /// Window, Wall Frame, Half Wall, legacy Pillar). Solid ground, a
        /// Foundation or a grounded pillar chain under its base make it a true
        /// support (returns true). Standing on a suspended deck makes it a
        /// pass-through: the deck's load state comes back in
        /// <paramref name="carryingDeck"/> so the caller continues that span
        /// instead of resetting it - this closes the floor-floor-wall ladder
        /// that restarted the cantilever forever. An unarmed legacy/restored
        /// deck counts as stable so old saves keep building normally.
        /// </summary>
        public static bool TryResolveSupportBase(PlacedTieredBlock support,
            out StructuralLoadState carryingDeck, int depth = 6)
        {
            carryingDeck = null;
            if (support == null || depth <= 0) return false;
            if (support.TryGetComponent<AdjustablePillar>(out var pillar))
                return pillar.IsSupportGrounded();

            // A wall's root line sits exactly on a deck edge or a foundation rim,
            // so a thin downward ray grazes those colliders along their boundary
            // face and misses them - which read as "standing on nothing" and made
            // a floor on a legitimately supported wall red. A small box straddling
            // the base line sees everything the wall actually stands on.
            Transform root = support.transform;
            Vector3 centre = root.position - root.up * 0.15f;
            var overlaps = Physics.OverlapBox(centre, new Vector3(0.6f, 0.25f, 0.6f),
                root.rotation, ~0, QueryTriggerInteraction.Ignore);

            int bestSpan = int.MaxValue;
            StructuralLoadState bestDeck = null;
            bool sawUnarmedDeck = false;
            for (int i = 0; i < overlaps.Length; i++)
            {
                Collider collider = overlaps[i];
                if (collider == null || collider.transform.IsChildOf(root)) continue;
                if (BuildSystemV2.IsDynamicBody(collider)) continue;

                var below = collider.GetComponentInParent<PlacedTieredBlock>();
                if (below == null) return true; // terrain or any static world surface
                if (below == support || below.definition == null) continue;
                BuildFamily family = below.definition.family;
                if (family == BuildFamily.Foundation) return true;
                if (below.TryGetComponent<AdjustablePillar>(out var pillarBelow))
                {
                    if (pillarBelow.IsSupportGrounded()) return true;
                    continue; // a hanging pillar carries nothing
                }
                if (IsVerticalSupport(family))
                {
                    if (TryResolveSupportBase(below, out var relayedDeck, depth - 1)) return true;
                    if (relayedDeck != null && relayedDeck.armed && relayedDeck.spanFromSupport < bestSpan)
                    {
                        bestSpan = relayedDeck.spanFromSupport;
                        bestDeck = relayedDeck;
                    }
                    continue;
                }
                if (BuildFamilyInfo.IsDeck(family))
                {
                    var load = below.GetComponent<StructuralLoadState>();
                    if (load == null || !load.armed) { sawUnarmedDeck = true; continue; }
                    if (load.spanFromSupport < bestSpan)
                    {
                        bestSpan = load.spanFromSupport;
                        bestDeck = load;
                    }
                }
                // Railings, doors, panes and other fittings carry nothing and are
                // deliberately not the "legacy stable" case, or an edge railing
                // would quietly reopen the wall ladder.
            }

            if (bestDeck != null)
            {
                carryingDeck = bestDeck;
                return false;
            }
            return sawUnarmedDeck; // legacy/restored decks count as stable
        }

        private void AdoptDirectSupport(PlacedTieredBlock support)
        {
            float height = support.definition.family == BuildFamily.Foundation
                ? 1.125f
                : support.definition.family == BuildFamily.HalfWall ? 2.8f : 5.625f;
            if (support.TryGetComponent<AdjustablePillar>(out var pillar)) height = pillar.currentHeight;
            spanFromSupport = 1;
            supportAnchor = support.transform.position + support.transform.up * height;
        }

        private bool ReachesLevel(PlacedTieredBlock support, BuildFamily suspendedFamily)
        {
            float height = support.definition.family == BuildFamily.Foundation
                ? 1.125f
                : support.definition.family == BuildFamily.HalfWall ? 2.8f : 5.625f;
            if (support.TryGetComponent<AdjustablePillar>(out var adjustablePillar))
                height = adjustablePillar.currentHeight;
            Vector3 top = support.transform.position + support.transform.up * height;
            if (suspendedFamily == BuildFamily.Stairs && support.TryGetComponent<AdjustablePillar>(out _))
            {
                float nearest = float.MaxValue;
                foreach (var collider in GetComponentsInChildren<Collider>(true))
                {
                    if (collider == null) continue;
                    nearest = Mathf.Min(nearest, Vector3.Distance(collider.ClosestPoint(top), top));
                }
                if (nearest <= 0.8f) return true;
            }
            Vector3 delta = transform.position - top;
            float vertical = Mathf.Abs(Vector3.Dot(delta, support.transform.up));
            Vector3 planar = delta - support.transform.up * Vector3.Dot(delta, support.transform.up);
            // A deck rests ON a pillar or wall top, but hangs one full module off
            // a Foundation SIDE - placement grants span one at exactly that
            // geometry, so the audit must reach it too or the deck it just
            // allowed is destroyed on the first check.
            float reach = BuildFamilyInfo.IsRoofPanel(suspendedFamily) ? 5.8f
                : support.definition.family == BuildFamily.Foundation ? 8.1f : 5.5f;
            return vertical < 0.85f && planar.sqrMagnitude <= reach * reach;
        }

        public static bool IsVerticalSupport(BuildFamily family)
            => family == BuildFamily.Wall || family == BuildFamily.Doorway
                || family == BuildFamily.Window || family == BuildFamily.WallFrame
                || family == BuildFamily.HalfWall || family == BuildFamily.Pillar;

        public static bool IsFitting(BuildFamily family)
            => family == BuildFamily.Door || family == BuildFamily.GarageDoor
                || family == BuildFamily.WindowPane || family == BuildFamily.HatchLid
                || family == BuildFamily.Gate || family == BuildFamily.BigGate;

        /// <summary>
        /// True when other armed pieces currently depend on this one: a fitting
        /// it hosts, an armed vertical piece standing on its body, an armed deck
        /// or roof hanging from its top, or a farther cantilever deck relaying
        /// span through it. Read by the inspection HUD; never affects physics.
        /// </summary>
        public static bool IsLoadBearing(PlacedTieredBlock piece)
        {
            if (piece == null || piece.definition == null) return false;
            BuildFamily family = piece.definition.family;
            if (IsFitting(family)) return false; // fittings carry nothing

            bool isSupport = family == BuildFamily.Foundation || IsVerticalSupport(family)
                || piece.GetComponent<AdjustablePillar>() != null;
            float topHeight = piece.TryGetComponent<AdjustablePillar>(out var pillar)
                ? pillar.currentHeight
                : family == BuildFamily.Foundation ? 1.125f
                : family == BuildFamily.HalfWall ? 2.8f : 5.625f;
            Vector3 up = piece.transform.up;
            Vector3 top = piece.transform.position + up * topHeight;
            var ownLoad = piece.GetComponent<StructuralLoadState>();
            bool pieceIsDeck = BuildFamilyInfo.IsDeck(family);
            int ownSpan = ownLoad != null && ownLoad.armed && !ownLoad.verticalPiece
                ? ownLoad.spanFromSupport : 0;

            var hits = Physics.OverlapSphere(piece.transform.position, 9f, ~0, QueryTriggerInteraction.Ignore);
            var visited = new HashSet<PlacedTieredBlock>();
            for (int i = 0; i < hits.Length; i++)
            {
                var block = hits[i] != null ? hits[i].GetComponentInParent<PlacedTieredBlock>() : null;
                if (block == null || block == piece || block.definition == null || !visited.Add(block)) continue;
                var load = block.GetComponent<StructuralLoadState>();
                if (load == null || !load.armed) continue;

                // A fitting held by this frame falls with it.
                if (load.fittingPiece)
                {
                    if (load.hostPiece == piece) return true;
                    continue;
                }

                // A hanging pillar depends on the block its top touches.
                if (load.pillarPiece)
                {
                    var blockPillar = block.GetComponent<AdjustablePillar>();
                    if (blockPillar != null && blockPillar.IsSupportGrounded()) continue;
                    float blockHeight = blockPillar != null ? blockPillar.currentHeight : 5.625f;
                    var tops = Physics.OverlapBox(
                        block.transform.position + block.transform.up * (blockHeight + 0.1f),
                        new Vector3(0.5f, 0.3f, 0.5f), block.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
                    for (int c = 0; c < tops.Length; c++)
                        if (tops[c] != null && tops[c].transform.IsChildOf(piece.transform)) return true;
                    continue;
                }

                // A vertical piece or railing whose base line rests on this body,
                // or a hanging piece whose head this body carries.
                if (load.verticalPiece)
                {
                    var below = Physics.OverlapBox(block.transform.position - block.transform.up * 0.15f,
                        new Vector3(0.6f, 0.25f, 0.6f), block.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
                    for (int c = 0; c < below.Length; c++)
                        if (below[c] != null && below[c].transform.IsChildOf(piece.transform)) return true;
                    if (!(TryResolveSupportBase(block, out var blockDeck) || blockDeck != null))
                    {
                        float blockHeight = block.definition.family == BuildFamily.HalfWall ? 2.8f : 5.625f;
                        var head = Physics.OverlapBox(
                            block.transform.position + block.transform.up * (blockHeight + 0.1f),
                            new Vector3(0.6f, 0.25f, 0.6f), block.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
                        for (int c = 0; c < head.Length; c++)
                            if (head[c] != null && head[c].transform.IsChildOf(piece.transform)) return true;
                    }
                    continue;
                }

                // An armed deck or roof hanging from this support's top.
                if (isSupport)
                {
                    Vector3 offset = block.transform.position - top;
                    float vertical = Vector3.Dot(offset, up);
                    float planar = (offset - up * vertical).magnitude;
                    if (planar <= 5.6f && Mathf.Abs(vertical) <= 1f) return true;
                }

                // A farther cantilever deck relaying its span through this one.
                if (pieceIsDeck && ownSpan > 0 && load.spanFromSupport > ownSpan
                    && Vector3.Distance(block.transform.position, piece.transform.position) <= 8.5f)
                    return true;
            }
            return false;
        }
    }
}
