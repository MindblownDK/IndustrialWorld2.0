// Assets/Scripts/VoxelEngine/GridSystem/GridBeacon.cs
//
// Grid Beacon — projects a visible vertical light beam into the sky that can be
// seen from far away. Toggle on/off via the terminal. Draws 10W.
//
// The beam is a stretched emissive cylinder that fades with distance.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Materials;

namespace VoxelEngine.GridSystem
{
    public class GridBeacon : GridBlock, IBeaconSource
    {
        private static readonly List<GridBeacon> s_all = new();
        /// <summary>Live beacons, for the orbital map. Powered ones paint as contacts.</summary>
        public static IReadOnlyList<GridBeacon> All => s_all;
        [Header("Beacon")]
        [Tooltip("Power consumed while active (W).")]
        public float powerDrawWatts = 10f;
        [Tooltip("Beam height in metres.")]
        public float beamHeight = 200f;
        [Tooltip("Beam colour.")]
        public Color beamColor = new Color(0.3f, 0.8f, 1f, 0.4f);
        [Tooltip("Rotation speed of the beacon light (degrees/sec).")]
        public float rotationSpeed = 90f;

        public bool IsActive { get; private set; }

        public override float PowerDraw => (Enabled && IsActive) ? powerDrawWatts : 0f;

        private GameObject _beam;
        private GameObject _beaconLight;
        private Light _pointLight;

        // ── Beacon identity (14.30.0-dev, milestone 10) ────────────────────
        // Minted at placement, persisted through the per-block runtime payload
        // the save file already carries, which is also what every sync channel
        // rides - so placement, rename, range and sharing replicate and survive
        // a rejoin with no second serializer.
        private string _beaconId = "";
        private string _ownerId = "";
        private string _beaconName = "";
        private BeaconShare _share = BeaconShare.Private;
        private float _rangeM;   // 0 = unlimited (the default)

        public string BeaconId => _beaconId;
        public string BeaconOwnerId => _ownerId;

        /// <summary>The marker name. Kept in lockstep with blockName so the
        /// master terminal and the HUD never disagree about what this is called.</summary>
        public string BeaconName
        {
            get => string.IsNullOrEmpty(_beaconName) ? blockName : _beaconName;
            set
            {
                _beaconName = string.IsNullOrWhiteSpace(value) ? "Beacon" : value.Trim();
                blockName = _beaconName;
            }
        }

        public BeaconShare BeaconShareMode { get => _share; set => _share = value; }
        public float BeaconRangeM { get => _rangeM; set => _rangeM = Mathf.Max(0f, value); }
        public bool BeaconLit => IsActive;
        public Vector3 BeaconWorldPosition => transform.position;

        public void RestoreBeaconIdentity(string id, string ownerId, string name, int share, float range)
        {
            // Id and owner only land when non-empty: a stale empty record must
            // never erase a freshly minted identity.
            if (!string.IsNullOrEmpty(id)) _beaconId = id;
            if (!string.IsNullOrEmpty(ownerId)) _ownerId = ownerId;
            if (!string.IsNullOrEmpty(name)) BeaconName = name;
            if (System.Enum.IsDefined(typeof(BeaconShare), (byte)share)) _share = (BeaconShare)share;
            _rangeM = Mathf.Max(0f, range);
        }

        public override void OnPlaced()
        {
            base.OnPlaced();
            BlockMass = 80f;
            maxHP = 200f;
            currentHP = maxHP;
            blockName = "Beacon";
            CreateBeam();
            IsActive = true;
            Register();

            // Stamp identity at placement. Remote and restored copies run this
            // too, harmlessly: the authoritative identity is applied right after
            // through the save-format payload and overwrites the stamp. Legacy
            // saves carry no payload and settle as host-owned, do-not-share -
            // which is exactly the fail-closed default.
            if (string.IsNullOrEmpty(_beaconId)) _beaconId = BeaconRoster.MintId();
            if (string.IsNullOrEmpty(_ownerId))
                _ownerId = VoxelEngine.Networking.NetworkSession.LocalPlayerId;
            if (string.IsNullOrEmpty(_beaconName)) _beaconName = blockName;
        }

        private void OnEnable() => Register();
        private void OnDisable() => Unregister();

        private void Register()
        {
            if (!s_all.Contains(this)) s_all.Add(this);
            BeaconRoster.Register(this);
        }

        private void Unregister()
        {
            s_all.Remove(this);
            BeaconRoster.Unregister(this);
        }

        private void Update()
        {
            bool powered = Grid != null && Grid.HasPower;
            bool active = Enabled && powered;

            if (active != IsActive)
            {
                IsActive = active;
                if (_beam != null) _beam.SetActive(active);
                if (_pointLight != null) _pointLight.enabled = active;
                if (_beaconLight != null) _beaconLight.SetActive(active);
            }

            // Rotate the beacon housing when active.
            if (IsActive && _beaconLight != null)
            {
                _beaconLight.transform.Rotate(0, rotationSpeed * Time.deltaTime, 0);
            }
        }

        private void CreateBeam()
        {
            float cs = EffectiveCellSize;

            // Vertical beam — a tall thin emissive cylinder.
            _beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _beam.name = "BeaconBeam";
            _beam.transform.SetParent(transform, false);
            _beam.transform.localPosition = new Vector3(0, beamHeight * 0.5f + cs * 0.3f, 0);
            _beam.transform.localScale = new Vector3(cs * 0.15f, beamHeight * 0.5f, cs * 0.15f);
            Object.DestroyImmediate(_beam.GetComponent<Collider>());

            var beamMat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            beamMat.color = beamColor;
            if (beamMat.HasProperty("_BaseColor")) beamMat.SetColor("_BaseColor", beamColor);
            if (beamMat.HasProperty("_EmissionColor"))
            {
                beamMat.EnableKeyword("_EMISSION");
                beamMat.SetColor("_EmissionColor", beamColor * 2f);
            }
            _beam.GetComponent<Renderer>().sharedMaterial = beamMat;

            // Rotating beacon light housing (the lamp that spins).
            _beaconLight = new GameObject("BeaconLamp");
            _beaconLight.transform.SetParent(transform, false);
            _beaconLight.transform.localPosition = new Vector3(0, cs * 0.35f, 0);

            var lampMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            lampMat.color = new Color(0.9f, 0.9f, 0.95f);
            if (lampMat.HasProperty("_BaseColor")) lampMat.SetColor("_BaseColor", lampMat.color);
            if (lampMat.HasProperty("_EmissionColor"))
            {
                lampMat.EnableKeyword("_EMISSION");
                lampMat.SetColor("_EmissionColor", beamColor * 1.5f);
            }

            var lamp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lamp.transform.SetParent(_beaconLight.transform, false);
            lamp.transform.localScale = new Vector3(cs * 0.12f, cs * 0.08f, cs * 0.12f);
            Object.DestroyImmediate(lamp.GetComponent<Collider>());
            lamp.GetComponent<Renderer>().sharedMaterial = lampMat;

            // Lens.
            var lens = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lens.transform.SetParent(_beaconLight.transform, false);
            lens.transform.localPosition = new Vector3(cs * 0.08f, 0, 0);
            lens.transform.localScale = Vector3.one * cs * 0.06f;
            Object.DestroyImmediate(lens.GetComponent<Collider>());
            lens.GetComponent<Renderer>().sharedMaterial = beamMat;

            // Point light.
            _pointLight = _beaconLight.AddComponent<Light>();
            _pointLight.type = LightType.Point;
            _pointLight.color = beamColor;
            _pointLight.range = 30f;
            _pointLight.intensity = 2f;
        }

        public override void OnRemoved()
        {
            Unregister();
            base.OnRemoved();
            if (_beam != null) Destroy(_beam);
            if (_beaconLight != null) Destroy(_beaconLight);
        }
    }
}
