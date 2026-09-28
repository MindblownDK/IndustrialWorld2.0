using UnityEngine;

namespace VoxelEngine.Building.Tiered
{
    /// <summary>
    /// Scales a pillar from its root to the deck it carries and tracks whether the
    /// pillar actually stands on something. A pillar hanging from a floor over a
    /// deep gap is placeable, but it carries no load until the chain of pillars
    /// beneath it reaches solid ground (or another supported piece). When terrain
    /// is mined out under a grounded pillar, the pillar grows downward - top edge
    /// fixed - to reconnect with the ground, up to its maximum height.
    /// </summary>
    public sealed class AdjustablePillar : MonoBehaviour
    {
        public const float AuthoredHeight = 5.625f;
        public const float MaximumHeight = AuthoredHeight * 1.5f;

        /// <summary>Slack under the root that still counts as standing on it.</summary>
        private const float GroundSlack = 0.6f;
        private const float ProbeLift = 0.1f;
        private const float CheckInterval = 1f;
        private const int MaximumChainDepth = 12;

        public float currentHeight = AuthoredHeight;

        private PlacedTieredBlock _placed;
        private float _nextCheck;
        private bool _grounded = true;
        private float _groundedValidUntil;

        // Recursion stamp so a malformed pillar loop can never chain forever.
        private static int s_probeStamp;
        private int _visitedStamp = -1;

        private void Start()
        {
            _placed = GetComponent<PlacedTieredBlock>();
            _nextCheck = Time.time + Random.Range(0.2f, CheckInterval);

            // Ghost previews never carry an initialized definition and must not
            // probe, resize or move themselves.
            if (_placed == null || _placed.definition == null) return;

            // Additive persistence: restored pieces carry their grounded root, so
            // a non-standard pillar can recover its height from the structure
            // directly above without adding a new save field. The first placed
            // piece wins: a chain stage meets the pillar root above it, a lone
            // pillar meets the deck underside, and anything else ends recovery
            // so a stage never stretches through its own chain.
            if (!Mathf.Approximately(currentHeight, AuthoredHeight)) return;
            var hits = Physics.RaycastAll(transform.position + transform.up * 0.05f,
                transform.up, 40f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                Collider collider = hits[i].collider;
                if (collider == null || collider.transform.IsChildOf(transform)) continue;
                var block = collider.GetComponentInParent<PlacedTieredBlock>();
                if (block == null || block == _placed) continue;
                if (block.definition == null) continue;
                BuildFamily family = block.definition.family;
                bool sizesToHit = block.GetComponent<AdjustablePillar>() != null
                    || family == BuildFamily.Floor || family == BuildFamily.FloorHatch;
                if (sizesToHit) Configure(hits[i].distance - 0.05f);
                return;
            }
        }

        private void Update()
        {
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + CheckInterval;

            // Ghost previews share this component but never carry a definition;
            // they must not probe or move themselves.
            if (_placed == null) _placed = GetComponent<PlacedTieredBlock>();
            if (_placed == null || _placed.definition == null) return;

            s_probeStamp++;
            _grounded = ProbeStanding(MaximumChainDepth);
            _groundedValidUntil = Time.time + 0.25f;
            if (!_grounded) GrowTowardGround();
        }

        public void Configure(float height)
        {
            currentHeight = Mathf.Clamp(height, 0.5f, MaximumHeight);
            Vector3 scale = transform.localScale;
            scale.y = currentHeight / AuthoredHeight;
            transform.localScale = scale;
        }

        /// <summary>
        /// True when this pillar can carry load: its root stands on terrain, a
        /// placed piece, or a chain of pillars that eventually touches down.
        /// </summary>
        public bool IsSupportGrounded()
        {
            // Structural audits query every pillar in range several times per
            // second; a quarter-second cache keeps that cheap without letting a
            // freshly grounded chain go unnoticed for long.
            if (Time.time < _groundedValidUntil) return _grounded;
            s_probeStamp++;
            _grounded = ProbeStanding(MaximumChainDepth);
            _groundedValidUntil = Time.time + 0.25f;
            return _grounded;
        }

        private bool ProbeStanding(int depth)
        {
            if (depth <= 0 || _visitedStamp == s_probeStamp) return false;
            _visitedStamp = s_probeStamp;

            var hits = Physics.RaycastAll(transform.position + transform.up * ProbeLift,
                -transform.up, GroundSlack + ProbeLift, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                Collider collider = hits[i].collider;
                if (collider == null || collider.transform.IsChildOf(transform)) continue;
                if (BuildSystemV2.IsDynamicBody(collider)) continue;

                var block = collider.GetComponentInParent<PlacedTieredBlock>();
                if (block == null) return true; // terrain or any static world surface

                var pillarBelow = block.GetComponent<AdjustablePillar>();
                if (pillarBelow != null)
                {
                    if (pillarBelow.ProbeStanding(depth - 1)) return true;
                    continue; // a hanging pillar below transfers nothing
                }
                if (block.definition != null) return true; // foundation, deck, stairs...
            }
            return false;
        }

        /// <summary>
        /// The ground under this pillar was mined away. Keep the top edge exactly
        /// where the structure expects it and extend the root downward until it
        /// touches ground again or the pillar reaches its maximum height.
        /// </summary>
        private void GrowTowardGround()
        {
            float available = MaximumHeight - currentHeight;
            if (available <= 0.01f) return;

            float growth = available;
            var hits = Physics.RaycastAll(transform.position + transform.up * ProbeLift,
                -transform.up, available + ProbeLift, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                Collider collider = hits[i].collider;
                if (collider == null || collider.transform.IsChildOf(transform)) continue;
                if (BuildSystemV2.IsDynamicBody(collider)) continue;
                growth = Mathf.Max(0f, hits[i].distance - ProbeLift);
                break;
            }
            if (growth <= 0.01f) return;

            float previous = currentHeight;
            Configure(currentHeight + growth);
            transform.position -= transform.up * (currentHeight - previous);
        }
    }
}
