// Assets/Scripts/VoxelEngine/Building/BridgeSpan.cs
//
// THE BRIDGE SPAN — the structure a water crossing belongs to.
//
// A `RoadRun` is a wear ledger: it groups cells that share a surface and bills them as one. A span
// is a different thing and deliberately not the same object. It groups the cells that cross ONE gap
// and owns what a crossing has and a wear pool does not — how high the deck stands above the water,
// where the piers go, and whether the deck can open for a ship.
//
// Both exist on the same cells at once, and that is not duplication: a viaduct of three crossings is
// one road (one run, one wear curve, one repair gesture) and three spans (three deck heights, three
// sets of piers, three things a barge can hit). Merging them would mean either a road that wears per
// crossing or a crossing that cannot be repaired as part of the road it carries.
//
// WHAT A SPAN IS MADE OF
//   • A DECK, which is just the bridge cells themselves — `AsphaltRoad` in its deck mode, flat
//     rather than draped, because a deck that follows the riverbed is a ford.
//   • PIERS, built down from the deck to whatever is under it at a regular spacing, so a long
//     crossing reads as a structure and not as a floating slab. They are visual and physical: a
//     pier has a collider, so a boat hits it.
//   • A STRUCTURE KIND. A culvert is a short crossing that hugs the ground and lets a stream run
//     under an arch; a bridge stands off on piers; a drawbridge is a bridge whose deck can open.
//
// THE DRAWBRIDGE
//   Opening does not move the placed cells. Moving a `PlacedBlock` means moving its collider, its
//   save position and its run membership, which is a lot of machinery to hang off a hinge — and a
//   cell that has moved is a cell the save no longer describes. Instead the span disables its cells
//   and raises two leaf meshes about hinges at the abutments. Open means there is genuinely no road:
//   nothing to drive on, nothing to collide with, and a clear channel underneath. Shut means the
//   cells come back exactly as they were.
//
// Deliberately NOT here yet: power draw, an automatic ship-approach trigger, and a warning light or
// horn. Those are open items on the roadmap, listed as such rather than stubbed.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Building
{
    /// <summary>What a crossing is. Appended-order enum: a save stores it as an int.</summary>
    public enum BridgeStructure
    {
        /// <summary>Short and low. Hugs the ground, lets a stream run under. Cheapest.</summary>
        Culvert,
        /// <summary>Stands off on piers over a real gap. Always shut.</summary>
        Fixed,
        /// <summary>A bridge whose deck opens for traffic on the water.</summary>
        Drawbridge
    }

    public sealed class BridgeSpan
    {
        // ════════════════════════════════════════════════════════════════
        //  AUTHORED TUNING
        // ════════════════════════════════════════════════════════════════

        /// <summary>Piers per metre of deck. At one per 8 m a 24 m crossing gets three, which reads
        /// as a structure without turning a small culvert into a colonnade.</summary>
        public const float METRES_PER_PIER = 8f;

        /// <summary>Below this deck clearance a crossing is a culvert rather than a bridge: there is
        /// not enough room under it for a pier to mean anything.</summary>
        public const float CULVERT_CLEARANCE = 1.5f;

        /// <summary>Seconds for a drawbridge to swing fully open or shut. Slow enough that a player
        /// on the approach can see it happening and stop.</summary>
        public const float SWING_SECONDS = 4f;

        /// <summary>Clearance assumed when the ground probe finds no bottom at all.
        /// `AsphaltRoad.ProbeGround` reaches 4 m down and no further, so a deck over a deep channel
        /// gets no reading whatsoever. Treating "no bottom found" as zero clearance would classify a
        /// genuine bridge as a culvert and leave it hanging with no piers — the exact opposite of
        /// what the missing reading means. Absence of ground is deep water, not shallow water.</summary>
        public const float NO_BOTTOM_CLEARANCE = 6f;

        /// <summary>How far a drawbridge leaf lifts, in degrees. 70 degrees leaves a clear channel
        /// and reads as a bascule rather than a ramp.</summary>
        public const float LEAF_ANGLE = 70f;

        // ════════════════════════════════════════════════════════════════
        //  STATE
        // ════════════════════════════════════════════════════════════════

        private readonly List<AsphaltRoad> _cells = new List<AsphaltRoad>(16);
        private readonly List<GameObject> _piers = new List<GameObject>(8);
        private GameObject _leafNear, _leafFar;
        private Material _leafMaterial;

        public BridgeStructure Structure { get; set; } = BridgeStructure.Fixed;

        /// <summary>Metres of clear water under the deck. Drives the culvert/bridge split and the
        /// pier height, and is what the player is really paying for.</summary>
        public float DeckClearance { get; set; }

        /// <summary>0 = deck down and drivable, 1 = fully open. Only meaningful on a drawbridge.</summary>
        public float Open01 { get; private set; }

        /// <summary>Where the span is heading. A shut span sits at 0 and an open one at 1; the value
        /// in between is the animation, not a state.</summary>
        public bool WantsOpen { get; private set; }

        public int CellCount => _cells.Count;
        public IReadOnlyList<AsphaltRoad> Cells => _cells;

        /// <summary>Deck length in metres, measured along the crossing.</summary>
        public float DeckLength
        {
            get
            {
                if (_cells.Count == 0) return 0f;
                float length = 0f;
                for (int i = 0; i < _cells.Count; i++)
                    if (_cells[i] != null) length += _cells[i].cellSize;
                return length;
            }
        }

        public bool CanOpen => Structure == BridgeStructure.Drawbridge && _cells.Count >= 2;

        public string StatusLabel
        {
            get
            {
                switch (Structure)
                {
                    case BridgeStructure.Culvert: return "Culvert";
                    case BridgeStructure.Drawbridge:
                        if (Open01 <= 0.01f) return "Drawbridge - closed to traffic";
                        if (Open01 >= 0.99f) return "Drawbridge - open to water";
                        return "Drawbridge - swinging";
                    default: return "Bridge";
                }
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  MEMBERSHIP
        // ════════════════════════════════════════════════════════════════

        /// <summary>Attaches a cell to this span.</summary>
        public void Adopt(AsphaltRoad cell)
        {
            if (cell == null || _cells.Contains(cell)) return;
            _cells.Add(cell);
            cell.AttachToSpan(this);
        }

        /// <summary>Detaches a cell without re-solving the span. Used when the cell is going away.</summary>
        public void Release(AsphaltRoad cell)
        {
            if (cell == null) return;
            _cells.Remove(cell);
        }

        public bool Contains(AsphaltRoad cell) => cell != null && _cells.Contains(cell);

        /// <summary>
        /// Joins the cell to the largest span among its bridge neighbours, absorbing the rest, or
        /// starts a new span. Same shape as `RoadRun.JoinOrCreate`, because a crossing that gets
        /// extended from either bank has to become one structure rather than two butted together.
        /// </summary>
        public static BridgeSpan JoinOrCreate(AsphaltRoad cell, List<AsphaltRoad> bridgeNeighbours)
        {
            BridgeSpan surviving = null;
            for (int i = 0; bridgeNeighbours != null && i < bridgeNeighbours.Count; i++)
            {
                var span = bridgeNeighbours[i]?.Span;
                if (span == null) continue;
                if (surviving == null || span.CellCount > surviving.CellCount) surviving = span;
            }
            if (surviving == null) surviving = new BridgeSpan();

            for (int i = 0; bridgeNeighbours != null && i < bridgeNeighbours.Count; i++)
            {
                var span = bridgeNeighbours[i]?.Span;
                if (span == null || span == surviving) continue;
                surviving.Absorb(span);
            }
            surviving.Adopt(cell);
            return surviving;
        }

        private void Absorb(BridgeSpan other)
        {
            if (other == null || other == this) return;
            for (int i = 0; i < other._cells.Count; i++)
            {
                var cell = other._cells[i];
                if (cell == null) continue;
                _cells.Add(cell);
                cell.AttachToSpan(this);
            }
            other._cells.Clear();
            // The longer structure's deck height and kind win: absorbing a culvert into a viaduct
            // must not lower the viaduct to the culvert.
            if (other.DeckClearance > DeckClearance) DeckClearance = other.DeckClearance;
            if (other.Structure > Structure) Structure = other.Structure;
            other.Teardown();
        }

        /// <summary>Drops the piers and leaves. Called when the span stops being a span.</summary>
        public void Teardown()
        {
            for (int i = 0; i < _piers.Count; i++)
                if (_piers[i] != null) Object.Destroy(_piers[i]);
            _piers.Clear();
            if (_leafNear != null) Object.Destroy(_leafNear);
            if (_leafFar != null) Object.Destroy(_leafFar);
            _leafNear = null; _leafFar = null;
        }

        // ════════════════════════════════════════════════════════════════
        //  STRUCTURE — classification, piers, leaves
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Classifies the span from what it actually spans and rebuilds its furniture. Called after
        /// the cells settle, so a crossing extended from three cells to thirty becomes a bridge on
        /// piers rather than staying a culvert.
        /// </summary>
        /// <param name="allowDrawbridge">Lets the span classify itself as a drawbridge. Off when the
        /// player has not asked for one, so a road that happens to cross a river does not silently
        /// become something that can open under a lorry.</param>
        public void Rebuild(bool allowDrawbridge, Material deckMaterial)
        {
            if (_cells.Count == 0) { Teardown(); return; }

            DeckClearance = MeasureClearance();
            var wanted = DeckClearance < CULVERT_CLEARANCE ? BridgeStructure.Culvert
                       : allowDrawbridge ? BridgeStructure.Drawbridge
                       : BridgeStructure.Fixed;
            // Never downgrade a span the player already opened, or reloading a save would shut a
            // drawbridge that a ship was on its way through.
            if (Structure == BridgeStructure.Drawbridge && wanted == BridgeStructure.Fixed)
                wanted = BridgeStructure.Drawbridge;
            Structure = wanted;

            BuildPiers();
            if (Structure == BridgeStructure.Drawbridge) BuildLeaves(deckMaterial);
            else if (_leafNear != null || _leafFar != null)
            {
                Object.Destroy(_leafNear); Object.Destroy(_leafFar);
                _leafNear = null; _leafFar = null;
            }
            ApplyOpen(Open01);
        }

        /// <summary>How far the deck stands above what is under it. A culvert over a streambed reads
        /// low; a bridge over a channel reads high; a deck with no bottom inside probe range reads
        /// as deep, because that is what a missing reading over water actually means.</summary>
        private float MeasureClearance()
        {
            float best = 0f;
            for (int i = 0; i < _cells.Count; i++)
            {
                var cell = _cells[i];
                if (cell == null) continue;
                Vector3 up = cell.transform.up;
                if (!AsphaltRoad.ProbeGround(cell.transform.position - up * 0.2f, up, out float below))
                {
                    if (NO_BOTTOM_CLEARANCE > best) best = NO_BOTTOM_CLEARANCE;
                    continue;
                }
                // ProbeGround reports ground ABOVE the sample point as positive, so ground below the
                // deck comes back negative and its magnitude is the clearance.
                if (-below > best) best = -below;
            }
            return best;
        }

        private void BuildPiers()
        {
            for (int i = 0; i < _piers.Count; i++)
                if (_piers[i] != null) Object.Destroy(_piers[i]);
            _piers.Clear();

            if (Structure == BridgeStructure.Culvert || _cells.Count == 0) return;

            int piers = Mathf.FloorToInt(DeckLength / Mathf.Max(1f, METRES_PER_PIER));
            if (piers <= 0) return;

            for (int p = 1; p <= piers; p++)
            {
                float along = p / (float)(piers + 1);
                var cell = _cells[Mathf.Clamp(Mathf.RoundToInt(along * (_cells.Count - 1)), 0, _cells.Count - 1)];
                if (cell == null) continue;

                Vector3 up = cell.transform.up;
                // No bottom inside probe range is deep water, not "nothing under it": the pier still
                // gets built, to the assumed depth, so a bridge over a channel does not read as a
                // slab floating in the air.
                float drop = AsphaltRoad.ProbeGround(cell.transform.position, up, out float ground)
                    ? -ground : NO_BOTTOM_CLEARANCE;
                if (drop <= 0.2f) continue;      // genuinely nothing to stand on and nothing to span

                float width = Mathf.Max(0.35f, cell.cellSize * 0.22f);
                var pier = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pier.name = "BridgePier";
                Object.Destroy(pier.GetComponent<Collider>());
                var box = pier.AddComponent<BoxCollider>();
                box.size = Vector3.one;
                pier.transform.position = cell.transform.position - up * (drop * 0.5f);
                pier.transform.rotation = cell.transform.rotation;
                pier.transform.localScale = new Vector3(width, drop, width);
                _piers.Add(pier);
            }
        }

        private void BuildLeaves(Material deckMaterial)
        {
            if (_cells.Count < 2) return;
            _leafMaterial = deckMaterial;
            // Two leaves hinged at the abutments, meeting in the middle: the classic bascule. Built
            // as plain quads rather than as the cells themselves, so opening never moves a block the
            // save has a position for.
            _leafNear ??= MakeLeaf("BridgeLeaf_Near");
            _leafFar ??= MakeLeaf("BridgeLeaf_Far");
        }

        private GameObject MakeLeaf(string name)
        {
            var go = new GameObject(name);
            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            if (_leafMaterial != null) renderer.sharedMaterial = _leafMaterial;
            var mesh = new Mesh { name = name + " (runtime)" };
            mesh.hideFlags = HideFlags.DontSave;
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f),
                new Vector3(0.5f, 0f, 1f), new Vector3(-0.5f, 0f, 1f)
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            filter.sharedMesh = mesh;
            return go;
        }

        // ════════════════════════════════════════════════════════════════
        //  OPENING
        // ════════════════════════════════════════════════════════════════

        /// <summary>Asks the span to open or shut. Returns false when it cannot, with the reason.</summary>
        public bool ToggleOpen(out string refusal)
        {
            refusal = null;
            if (Structure != BridgeStructure.Drawbridge)
            { refusal = "That crossing cannot open"; return false; }
            if (_cells.Count < 2)
            { refusal = "A drawbridge needs at least two deck cells"; return false; }
            WantsOpen = !WantsOpen;
            return true;
        }

        /// <summary>Restores a saved open state without animating through it.</summary>
        public void SetOpenImmediate(float open01)
        {
            Open01 = Mathf.Clamp01(open01);
            WantsOpen = Open01 > 0.5f;
            ApplyOpen(Open01);
        }

        /// <summary>Advances the swing. Called from the owning cell's update, which already runs on
        /// a stagger, so a span costs nothing while it sits still.</summary>
        public void Tick(float deltaTime)
        {
            float target = WantsOpen ? 1f : 0f;
            if (Mathf.Approximately(Open01, target)) return;
            Open01 = Mathf.MoveTowards(Open01, target, deltaTime / Mathf.Max(0.25f, SWING_SECONDS));
            ApplyOpen(Open01);
        }

        /// <summary>Pushes the open fraction into the world: leaves up, and the deck cells out of
        /// the way so nothing drives into a channel that is supposed to be open.</summary>
        private void ApplyOpen(float open01)
        {
            bool passable = open01 < 0.5f;
            for (int i = 0; i < _cells.Count; i++)
            {
                var cell = _cells[i];
                if (cell == null) continue;
                cell.SetDeckPassable(passable);
            }

            if (_leafNear == null || _leafFar == null || _cells.Count < 2) return;
            var first = _cells[0];
            var last = _cells[_cells.Count - 1];
            if (first == null || last == null) return;

            float half = DeckLength * 0.5f;
            float angle = open01 * LEAF_ANGLE;
            PlaceLeaf(_leafNear, first.transform, half, -angle);
            PlaceLeaf(_leafFar, last.transform, half, angle);
        }

        private static void PlaceLeaf(GameObject leaf, Transform abutment, float length, float angle)
        {
            leaf.transform.position = abutment.position;
            leaf.transform.rotation = abutment.rotation * Quaternion.Euler(angle, 0f, 0f);
            leaf.transform.localScale = new Vector3(Mathf.Max(0.2f, abutment.localScale.x), 1f,
                                                    Mathf.Max(0.2f, length));
        }
    }
}
