// Assets/Scripts/VoxelEngine/Transport/TransportDrone.cs
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Transport
{
    /// <summary>
    /// The visible drone that flies a payload between two <see cref="DronePort"/>s.
    ///
    /// This is deliberately PRESENTATION ONLY. The delivery is already decided and paid for
    /// the moment <see cref="DronePort.Dispatch"/> is called: the cargo has left the source
    /// chests and the landing is scheduled by a timer. The drone simply shows what is
    /// happening, and is created and destroyed by the port that owns it.
    ///
    /// Keeping it presentation-only matters. If the simulation waited on the model — its
    /// collisions, its pathing, its chunk staying loaded — then a drone that clipped terrain
    /// or streamed out would strand real items. Instead the flight is authoritative and the
    /// mesh follows it, so switching the visuals off changes nothing about the logistics.
    ///
    /// Built from primitives so it needs no art asset: a body, four rotor discs and a small
    /// cargo block that only appears on the outbound leg.
    /// </summary>
    [DefaultExecutionOrder(62)]
    public class TransportDrone : MonoBehaviour
    {
        [Tooltip("How high above the straight line the drone arcs, in metres.")]
        public float cruiseHeight = 12f;

        [Tooltip("Degrees per second the rotors spin.")]
        public float rotorSpeed = 1440f;

        private Vector3 _from;
        private Vector3 _to;
        private Transform[] _rotors;
        private Transform _cargo;
        private DronePort _owner;

        /// <summary>
        /// Spawn a drone for a trip. Returns null when the visual is switched off for this
        /// port, which callers must tolerate — the flight still happens either way.
        /// </summary>
        public static TransportDrone Spawn(DronePort owner, Vector3 from, Vector3 to, ItemDefinition cargoItem)
        {
            if (owner == null || !owner.showDrone) return null;

            var go = new GameObject("TransportDrone");
            go.transform.position = from;

            var drone = go.AddComponent<TransportDrone>();
            drone._owner = owner;
            drone._from = from;
            drone._to = to;
            drone.Build(cargoItem);
            return drone;
        }

        private void Build(ItemDefinition cargoItem)
        {
            // Modelled on a heavy-lift octocopter: a white fuselage shell over a dark
            // carbon frame, eight arms in a radial ring each carrying a two-blade rotor,
            // landing skids underneath and a gimballed payload pod slung at the nose.
            var shell  = new Color(0.90f, 0.91f, 0.93f);   // white composite body
            var carbon = new Color(0.14f, 0.15f, 0.17f);   // dark arms and frame
            var metal  = new Color(0.55f, 0.57f, 0.60f);   // skids and hardware

            // ── Fuselage: a rounded core with a raised spine ────────────────
            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "Fuselage";
            core.transform.SetParent(transform, false);
            core.transform.localScale = new Vector3(0.82f, 0.46f, 1.05f);
            Strip(core, shell);

            var spine = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spine.name = "Spine";
            spine.transform.SetParent(transform, false);
            spine.transform.localPosition = new Vector3(0f, 0.20f, -0.05f);
            spine.transform.localScale = new Vector3(0.34f, 0.16f, 0.66f);
            Strip(spine, carbon);

            // ── Eight arms in a radial ring, each with a rotor ──────────────
            _rotors = new Transform[8];
            const float armLength = 1.15f;
            for (int i = 0; i < 8; i++)
            {
                float deg = i * 45f + 22.5f;               // offset so none points dead ahead
                float rad = deg * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                Vector3 hub = dir * armLength;

                var arm = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                arm.name = "Arm" + i;
                arm.transform.SetParent(transform, false);
                arm.transform.localPosition = dir * (armLength * 0.5f);
                arm.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
                arm.transform.localScale = new Vector3(0.075f, armLength * 0.5f, 0.075f);
                Strip(arm, carbon);

                // Motor can at the end of each arm.
                var motor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                motor.name = "Motor" + i;
                motor.transform.SetParent(transform, false);
                motor.transform.localPosition = hub + new Vector3(0f, 0.07f, 0f);
                motor.transform.localScale = new Vector3(0.13f, 0.08f, 0.13f);
                Strip(motor, shell);

                // Rotor hub, with two slender blades so the spin reads clearly.
                var rotor = new GameObject("Rotor" + i);
                rotor.transform.SetParent(transform, false);
                rotor.transform.localPosition = hub + new Vector3(0f, 0.15f, 0f);

                for (int b = 0; b < 2; b++)
                {
                    var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    blade.name = "Blade" + b;
                    blade.transform.SetParent(rotor.transform, false);
                    blade.transform.localPosition = new Vector3(b == 0 ? 0.30f : -0.30f, 0f, 0f);
                    blade.transform.localScale = new Vector3(0.62f, 0.012f, 0.085f);
                    Strip(blade, carbon);
                }
                _rotors[i] = rotor.transform;
            }

            // ── Landing skids ──────────────────────────────────────────────
            for (int side = -1; side <= 1; side += 2)
            {
                var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rail.name = "Skid" + side;
                rail.transform.SetParent(transform, false);
                rail.transform.localPosition = new Vector3(0.42f * side, -0.52f, 0f);
                rail.transform.localScale = new Vector3(0.07f, 0.055f, 1.25f);
                Strip(rail, metal);

                for (int end = -1; end <= 1; end += 2)
                {
                    var leg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    leg.name = "Leg";
                    leg.transform.SetParent(transform, false);
                    leg.transform.localPosition = new Vector3(0.34f * side, -0.30f, 0.42f * end);
                    leg.transform.localScale = new Vector3(0.055f, 0.42f, 0.055f);
                    Strip(leg, metal);
                }
            }

            // ── Gimballed payload pod, tinted to the cargo ─────────────────
            var gimbal = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            gimbal.name = "Gimbal";
            gimbal.transform.SetParent(transform, false);
            gimbal.transform.localPosition = new Vector3(0f, -0.30f, 0.46f);
            gimbal.transform.localScale = new Vector3(0.24f, 0.24f, 0.24f);
            Strip(gimbal, carbon);

            var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crate.name = "Cargo";
            crate.transform.SetParent(transform, false);
            crate.transform.localPosition = new Vector3(0f, -0.52f, 0f);
            crate.transform.localScale = new Vector3(0.46f, 0.36f, 0.52f);
            Strip(crate, cargoItem != null ? cargoItem.iconTint : new Color(0.82f, 0.70f, 0.40f));
            _cargo = crate.transform;
        }

        /// <summary>Primitives arrive with a collider; a purely decorative drone must not have one.</summary>
        private static void Strip(GameObject go, Color tint)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var rend = go.GetComponent<Renderer>();
            if (rend == null) return;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(sh) { color = tint };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            rend.material = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void Update()
        {
            if (_owner == null) { Destroy(gameObject); return; }

            // The port's own timer is the authority; the drone reads its progress rather
            // than keeping a clock of its own, so the two can never drift apart.
            float t = _owner.FlightProgress;

            // Outbound on the first half, home empty on the second.
            bool outbound = t <= 0.5f;
            float leg = outbound ? (t / 0.5f) : ((t - 0.5f) / 0.5f);
            Vector3 a = outbound ? _from : _to;
            Vector3 b = outbound ? _to   : _from;

            Vector3 flat = Vector3.Lerp(a, b, leg);
            // A simple parabola gives it a believable climb and descent.
            float lift = Mathf.Sin(Mathf.Clamp01(leg) * Mathf.PI) * cruiseHeight;
            Vector3 up = (flat - GravityCentre()).normalized;
            if (up.sqrMagnitude < 0.01f) up = Vector3.up;

            transform.position = flat + up * lift;

            Vector3 travel = b - a;
            if (travel.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(travel.normalized, up);

            if (_cargo != null && _cargo.gameObject.activeSelf != outbound)
                _cargo.gameObject.SetActive(outbound);

            if (_rotors != null)
                foreach (var r in _rotors)
                    if (r != null) r.Rotate(Vector3.up, rotorSpeed * Time.deltaTime, Space.Self);
        }

        /// <summary>
        /// The planet centre, so "up" is correct on a spherical world. Falls back to straight
        /// up when no body is active.
        /// </summary>
        private Vector3 GravityCentre()
        {
            var body = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            return body != null ? body.transform.position : transform.position - Vector3.up;
        }

        /// <summary>Remove the drone when its trip ends.</summary>
        public void Retire()
        {
            if (this != null && gameObject != null) Destroy(gameObject);
        }
    }
}
