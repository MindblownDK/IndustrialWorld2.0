// Assets/Scripts/VoxelEngine/GridSystem/GridIdentity.cs
//
// Names and classifies a grid.
//
// Every other part of the orbital map programme depends on this one component:
// the map needs a label to draw, the satellite systems need to know a construct
// has been declared a satellite, and the research gate needs to find satellite
// research stations. Doing it as its own component rather than fields on
// GridEntity keeps the physics class from growing a UI/metadata concern, and
// lets a grid that has never been named cost nothing at all.
//
// A grid with no GridIdentity behaves exactly as before and reports a generated
// fallback name, so this is fully save-compatible.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.GridSystem
{
    /// <summary>
    /// What the player has declared this construct to be. This is a DECLARATION, not a
    /// detection: the player marks a grid as a satellite, and the game then holds it to
    /// the requirements of one. A satellite is not a separate entity type — it is an
    /// ordinary grid wearing a different label.
    /// </summary>
    public enum GridClass
    {
        /// <summary>Default. A ship, rover, or anything else the player builds and drives.</summary>
        Vessel = 0,
        /// <summary>Declared a satellite: expected to hold an orbit and run sensor payloads.</summary>
        Satellite = 1,
        /// <summary>Declared a station: a large permanent construct, crewed or otherwise.</summary>
        Station = 2,
    }

    [DisallowMultipleComponent]
    public sealed class GridIdentity : MonoBehaviour
    {
        // ── Registry ─────────────────────────────────────────────────────────────
        // The orbital map iterates named constructs every time it opens, and satellite
        // services poll for active satellites. A static registry keeps both off
        // FindObjectsByType, which would otherwise sweep the whole scene per query.
        private static readonly List<GridIdentity> s_all = new();
        public static IReadOnlyList<GridIdentity> All => s_all;

        [Header("Identity")]
        [Tooltip("Player-facing name. Empty falls back to an auto-generated designation.")]
        [SerializeField] private string _displayName = "";

        [Tooltip("What the player has declared this construct to be.")]
        [SerializeField] private GridClass _gridClass = GridClass.Vessel;

        /// <summary>Raised when the name or class changes, so open UI can refresh.</summary>
        public static event System.Action<GridIdentity> OnIdentityChanged;

        private GridEntity _grid;
        private string _fallbackName;

        /// <summary>The grid this identity labels.</summary>
        public GridEntity Grid
        {
            get
            {
                if (_grid == null) _grid = GetComponent<GridEntity>();
                return _grid;
            }
        }

        /// <summary>
        /// The name to show the player. Never empty: an unnamed construct gets a stable
        /// generated designation so the orbital map never draws a blank label.
        /// </summary>
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_displayName)) return _displayName;
                return _fallbackName ??= GenerateFallbackName();
            }
        }

        /// <summary>True when the player has actually named this construct.</summary>
        public bool HasCustomName => !string.IsNullOrWhiteSpace(_displayName);

        public GridClass Class => _gridClass;
        public bool IsSatellite => _gridClass == GridClass.Satellite;
        public bool IsStation => _gridClass == GridClass.Station;

        /// <summary>Short tag for the map legend.</summary>
        public string ClassLabel => _gridClass switch
        {
            GridClass.Satellite => "SATELLITE",
            GridClass.Station => "STATION",
            _ => "VESSEL",
        };

        // ── Mutation ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Renames the construct. Trimmed and length-capped, because the name is drawn on
        /// the orbital map and an unbounded string would wreck that layout.
        /// </summary>
        public void SetDisplayName(string value)
        {
            string trimmed = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
            if (trimmed.Length > MaxNameLength) trimmed = trimmed.Substring(0, MaxNameLength);
            if (_displayName == trimmed) return;
            _displayName = trimmed;
            OnIdentityChanged?.Invoke(this);
        }

        public const int MaxNameLength = 28;

        public void SetGridClass(GridClass value)
        {
            if (_gridClass == value) return;
            _gridClass = value;
            OnIdentityChanged?.Invoke(this);
        }

        // ── Lifecycle ────────────────────────────────────────────────────────────
        private void OnEnable()
        {
            if (!s_all.Contains(this)) s_all.Add(this);
        }

        private void OnDisable()
        {
            s_all.Remove(this);
        }

        /// <summary>
        /// Fetches the identity for a grid, creating it on demand. Call this from anything
        /// that wants to NAME or CLASSIFY a grid; use <see cref="Find"/> to merely read.
        /// </summary>
        public static GridIdentity Ensure(GridEntity grid)
        {
            if (grid == null) return null;
            var identity = grid.GetComponent<GridIdentity>();
            if (identity == null) identity = grid.gameObject.AddComponent<GridIdentity>();
            return identity;
        }

        /// <summary>Reads the identity of a grid without creating one. May return null.</summary>
        public static GridIdentity Find(GridEntity grid)
        {
            return grid != null ? grid.GetComponent<GridIdentity>() : null;
        }

        /// <summary>
        /// The display name for any grid, whether or not it has been named. Safe on null.
        /// This is the call the orbital map and HUDs should use.
        /// </summary>
        public static string NameOf(GridEntity grid)
        {
            if (grid == null) return "Unknown";
            var identity = Find(grid);
            return identity != null ? identity.DisplayName : FallbackFor(grid);
        }

        /// <summary>The declared class of any grid. Unlabelled grids are vessels.</summary>
        public static GridClass ClassOf(GridEntity grid)
        {
            var identity = Find(grid);
            return identity != null ? identity.Class : GridClass.Vessel;
        }

        // ── Fallback naming ──────────────────────────────────────────────────────
        // Derived from the grid's own identity rather than a global counter, so the same
        // construct keeps the same designation across a save/load round trip even though
        // the fallback itself is never written to disk.
        private string GenerateFallbackName() => FallbackFor(Grid);

        private static string FallbackFor(GridEntity grid)
        {
            if (grid == null) return "Unknown";
            int hash = grid.gameObject.name.GetHashCode() ^ grid.BlockCount * 397;
            int serial = Mathf.Abs(hash) % 9000 + 1000;
            string prefix = grid.gridSize == GridSize.Small ? "SC" : "LC";
            return $"{prefix}-{serial}";
        }
    }
}
