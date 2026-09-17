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
            var bodyTint = new Color(0.30f, 0.62f, 0.72f);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(transform, false);
            body.transform.localScale = new Vector3(0.9f, 0.25f, 0.9f);
            Strip(body, bodyTint);

            _rotors = new Transform[4];
            var offsets = new[]
            {
                new Vector3( 0.6f, 0.18f,  0.6f),
                new Vector3(-0.6f, 0.18f,  0.6f),
                new Vector3( 0.6f, 0.18f, -0.6f),
                new Vector3(-0.6f, 0.18f, -0.6f),
            };
            for (int i = 0; i < 4; i++)
            {
                var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
                arm.name = "Arm" + i;
                arm.transform.SetParent(transform, false);
                arm.transform.localPosition = offsets[i] * 0.5f;
                arm.transform.localScale = new Vector3(0.6f, 0.07f, 0.12f);
                arm.transform.localRotation = Quaternion.LookRotation(offsets[i].normalized, Vector3.up);
                Strip(arm, bodyTint * 0.7f);

                var rotor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                rotor.name = "Rotor" + i;
                rotor.transform.SetParent(transform, false);
                rotor.transform.localPosition = offsets[i];
                rotor.transform.localScale = new Vector3(0.45f, 0.012f, 0.45f);
                Strip(rotor, new Color(0.75f, 0.80f, 0.85f, 1f));
                _rotors[i] = rotor.transform;
            }

            // The slung cargo crate, tinted to the item it represents.
            var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crate.name = "Cargo";
            crate.transform.SetParent(transform, false);
            crate.transform.localPosition = new Vector3(0f, -0.28f, 0f);
            crate.transform.localScale = new Vector3(0.42f, 0.34f, 0.42f);
            Strip(crate, cargoItem != null ? cargoItem.iconTint : new Color(0.8f, 0.7f, 0.4f));
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
