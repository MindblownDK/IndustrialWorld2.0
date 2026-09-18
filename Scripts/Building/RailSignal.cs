// Assets/Scripts/VoxelEngine/Building/RailSignal.cs
//
// A trackside signal — the visible half of block occupancy.
//
// WHY THIS EXISTS WHEN SECTIONS ARE ALREADY AUTOMATIC
// `RailSignalling` derives sections from the graph and trains already stop for occupied
// track without any player input. So a signal is deliberately NOT load-bearing: removing
// every signal in the world changes nothing about whether trains collide.
//
// What it does is make an invisible rule legible. A player watching a train stop for no
// apparent reason has been given a puzzle, not a mechanic. A red lamp at the point it
// stops turns the same event into an explanation - and lets them see, before committing
// to a schedule, which parts of their network are contended.
//
// That is the whole job: it reads state, it never changes it. Which also means it cannot
// desynchronise from the thing it reports.

using UnityEngine;

namespace VoxelEngine.Building
{
    [DisallowMultipleComponent, RequireComponent(typeof(PlacedBlock))]
    public class RailSignal : MonoBehaviour
    {
        [Header("Signal")]
        [Tooltip("How far to search for the track this signal watches.")]
        public float trackSearchRadius = 4f;

        [Tooltip("Renderer tinted to show the aspect. Assigned by the setup step.")]
        public Renderer lamp;

        [Header("Aspects")]
        public Color clearColor = new(0.25f, 0.90f, 0.35f);
        public Color occupiedColor = new(0.95f, 0.25f, 0.20f);
        public Color unknownColor = new(0.45f, 0.45f, 0.48f);

        /// <summary>The cell this signal watches, resolved lazily.</summary>
        public RailTrack WatchedTrack
        {
            get
            {
                if (_track == null)
                    _track = RailNetwork.FindNearest(transform.position, trackSearchRadius);
                return _track;
            }
        }
        private RailTrack _track;

        /// <summary>True when the section this signal guards is held by a train.</summary>
        public bool IsOccupied { get; private set; }

        /// <summary>True when there is no track to watch at all.</summary>
        public bool HasTrack => WatchedTrack != null;

        public string StatusLabel =>
            !HasTrack ? "No track in range"
            : IsOccupied ? "OCCUPIED - a train holds this section"
            : "CLEAR";

        private float _pollTimer;
        private MaterialPropertyBlock _block;

        private void Update()
        {
            // Polled rather than event-driven: occupancy changes constantly while a train
            // runs, and a few times a second is far more than enough for a lamp. An event
            // per claim would fire dozens of times a second for zero visible gain.
            _pollTimer -= Time.deltaTime;
            if (_pollTimer > 0f) return;
            _pollTimer = 0.25f;

            Refresh();
        }

        private void Refresh()
        {
            var track = WatchedTrack;
            if (track == null)
            {
                IsOccupied = false;
                ApplyLamp(unknownColor);
                return;
            }

            // Ask with a null claimant so the answer is "is anyone in here", rather than
            // "may I enter" - a signal is an observer and owns no claim of its own.
            IsOccupied = !RailSignalling.CanEnter(track, SignalProbe);
            ApplyLamp(IsOccupied ? occupiedColor : clearColor);
        }

        /// <summary>
        /// A stable, never-claiming identity used purely to test occupancy.
        ///
        /// Passing the signal itself would work today, but any future code that claims on
        /// behalf of the asker would silently let a signal reserve track. A dedicated
        /// sentinel makes that mistake impossible.
        /// </summary>
        private static readonly object SignalProbe = new();

        private void ApplyLamp(Color color)
        {
            if (lamp == null) return;

            // A property block rather than a material instance: one signal per section on a
            // large network is a lot of blocks, and instancing a material each would leak a
            // material per signal.
            _block ??= new MaterialPropertyBlock();
            lamp.GetPropertyBlock(_block);
            if (lamp.sharedMaterial != null && lamp.sharedMaterial.HasProperty("_BaseColor"))
                _block.SetColor("_BaseColor", color);
            _block.SetColor("_Color", color);
            lamp.SetPropertyBlock(_block);
        }
    }
}
