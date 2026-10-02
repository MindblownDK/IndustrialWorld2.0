// Assets/Scripts/VoxelEngine/GridSystem/StationaryRadarBeacon.cs
//
// Stationary Radar Beacon — a world-placed tall tower with a rotating radar
// dish on top + a visible beacon beam. Looks like a coastal radar station.
// Toggle on/off, draws 10W.
//
// 14.30.0-dev (milestone 10): the tower is a real beacon now. It carries an
// owner, a name, a marker range and a share rule (IBeaconSource), opens a
// panel on right-click, and its settings ride the same save-format machine
// runtime payload that already syncs every static machine - so a rename on
// one screen lands on all of them and survives a rejoin.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class StationaryRadarBeacon : MonoBehaviour, IBeaconSource
    {
        [Header("Radar Beacon")]
        public float powerDrawWatts = 10f;
        public float beamHeight = 150f;
        public Color beamColor = new Color(0.3f, 0.85f, 1f, 0.35f);
        public float dishRotationSpeed = 45f;
        public bool isOn = true;

        private GameObject _beam;
        private GameObject _dish;
        private Light _beaconLight;
        private Material _beamMat;
        private Material _sensorMat;
        private bool _isGhost;   // build-preview copy: inert, never a real beacon

        // ── Beacon identity (14.30.0-dev) ──────────────────────────────────
        private string _beaconId = "";
        private string _ownerId = "";
        private string _beaconName = "";
        private BeaconShare _share = BeaconShare.Private;
        private float _rangeM;   // 0 = unlimited (the default)
        private bool _stampedLocally;

        public string BeaconId => _beaconId;
        public string BeaconOwnerId => _ownerId;

        public string BeaconName
        {
            get => string.IsNullOrEmpty(_beaconName) ? "Radar Beacon" : _beaconName;
            set => _beaconName = string.IsNullOrWhiteSpace(value) ? "Radar Beacon" : value.Trim();
        }

        public BeaconShare BeaconShareMode { get => _share; set => _share = value; }
        public float BeaconRangeM { get => _rangeM; set => _rangeM = Mathf.Max(0f, value); }
        public bool BeaconLit => isOn;
        public Vector3 BeaconWorldPosition => transform.position;

        /// <summary>beamColor is the single source of truth for the tint; the
        /// setter keeps the authored alpha and retints beam, sensor lamp and
        /// light in place.</summary>
        public Color BeaconTint
        {
            get => beamColor;
            set
            {
                beamColor = new Color(value.r, value.g, value.b, beamColor.a);
                RefreshTint();
            }
        }

        private void RefreshTint()
        {
            if (_beamMat != null)
            {
                _beamMat.color = beamColor;
                if (_beamMat.HasProperty("_BaseColor")) _beamMat.SetColor("_BaseColor", beamColor);
            }
            if (_sensorMat != null && _sensorMat.HasProperty("_EmissionColor"))
                _sensorMat.SetColor("_EmissionColor", beamColor * 2f);
            if (_beaconLight != null) _beaconLight.color = beamColor;
        }

        public void RestoreBeaconIdentity(string id, string ownerId, string name, int share, float range)
        {
            if (!string.IsNullOrEmpty(id)) _beaconId = id;
            if (!string.IsNullOrEmpty(ownerId)) _ownerId = ownerId;
            if (!string.IsNullOrEmpty(name)) BeaconName = name;
            if (System.Enum.IsDefined(typeof(BeaconShare), (byte)share)) _share = (BeaconShare)share;
            _rangeM = Mathf.Max(0f, range);
        }

        private void Awake()
        {
            CreateVisuals();

            // A build-preview ghost is a drawing of a tower, not a tower: no
            // identity stamp, no roster entry, no sky-beam over the preview,
            // no simulation. (Awake runs mid-Instantiate, while BuildSystem
            // still holds the creating-ghost latch.)
            if (VoxelEngine.Building.BuildSystem.IsCreatingGhost)
            {
                _isGhost = true;
                if (_beam != null) _beam.SetActive(false);
                if (_beaconLight != null) _beaconLight.enabled = false;
                enabled = false;
                return;
            }

            // Stamp identity only on a GENUINE local placement. A copy spawned
            // from the network is inside BlockSync's apply guard and stays
            // unowned until the placer's announced identity lands - an unowned
            // beacon is visible to nobody, which fails closed. A copy restored
            // from a save is stamped here too, then immediately overwritten by
            // the persisted identity; legacy saves without one settle as
            // host-owned, do-not-share.
            if (!VoxelEngine.Networking.BlockSync.IsApplyingRemote && string.IsNullOrEmpty(_ownerId))
            {
                _beaconId = BeaconRoster.MintId();
                _ownerId = VoxelEngine.Networking.NetworkSession.LocalPlayerId;
                _stampedLocally = true;
            }
        }

        private void Start()
        {
            // A guest's freshly placed tower must announce its identity: the
            // machine-state poll only sends blocks inside the interaction
            // window, so placement opens that window explicitly.
            if (_stampedLocally)
            {
                var placed = GetComponentInParent<VoxelEngine.Building.PlacedBlock>();
                if (placed != null)
                    VoxelEngine.Networking.ContainerSync.NotifyLocalInteraction(placed);
            }
        }

        private void OnEnable() { if (!_isGhost) BeaconRoster.Register(this); }
        private void OnDisable() => BeaconRoster.Unregister(this);

        private void Update()
        {
            if (isOn)
            {
                if (_beam != null && !_beam.activeSelf) _beam.SetActive(true);
                if (_beaconLight != null) _beaconLight.enabled = true;
                if (_dish != null) _dish.transform.Rotate(0, dishRotationSpeed * Time.deltaTime, 0);
            }
            else
            {
                if (_beam != null) _beam.SetActive(false);
                if (_beaconLight != null) _beaconLight.enabled = false;
            }
        }

        private void CreateVisuals()
        {
            // Tower mast (tall cylinder).
            var mastMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mastMat.color = new Color(0.45f, 0.46f, 0.50f);
            if (mastMat.HasProperty("_BaseColor")) mastMat.SetColor("_BaseColor", mastMat.color);
            mastMat.SetFloat("_Metallic", 0.7f);
            mastMat.SetFloat("_Smoothness", 0.4f);

            var mast = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            mast.name = "Mast";
            mast.transform.SetParent(transform, false);
            mast.transform.localPosition = new Vector3(0, 3f, 0);
            mast.transform.localScale = new Vector3(0.3f, 3f, 0.3f);
            mast.GetComponent<Renderer>().sharedMaterial = mastMat;

            // Lattice supports.
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f * Mathf.Deg2Rad;
                var strut = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                strut.name = $"Strut_{i}";
                strut.transform.SetParent(transform, false);
                strut.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.8f, 1.5f, Mathf.Sin(a) * 0.8f);
                strut.transform.localScale = new Vector3(0.08f, 3.5f, 0.08f);
                strut.transform.localRotation = Quaternion.LookRotation(new Vector3(-Mathf.Cos(a), 0.5f, -Mathf.Sin(a)), Vector3.up);
                Object.DestroyImmediate(strut.GetComponent<Collider>());
                strut.GetComponent<Renderer>().sharedMaterial = mastMat;
            }

            // Platform at top.
            var platform = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            platform.name = "Platform";
            platform.transform.SetParent(transform, false);
            platform.transform.localPosition = new Vector3(0, 6.1f, 0);
            platform.transform.localScale = new Vector3(1.2f, 0.15f, 1.2f);
            Object.DestroyImmediate(platform.GetComponent<Collider>());
            platform.GetComponent<Renderer>().sharedMaterial = mastMat;

            // Rotating radar dish.
            _dish = new GameObject("RadarDish");
            _dish.transform.SetParent(transform, false);
            _dish.transform.localPosition = new Vector3(0, 6.5f, 0);

            var dishMat = new Material(mastMat);
            dishMat.color = new Color(0.6f, 0.62f, 0.65f);
            if (dishMat.HasProperty("_BaseColor")) dishMat.SetColor("_BaseColor", dishMat.color);

            var dish = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dish.name = "DishMesh";
            dish.transform.SetParent(_dish.transform, false);
            dish.transform.localScale = new Vector3(1.5f, 0.3f, 1.5f);
            Object.DestroyImmediate(dish.GetComponent<Collider>());
            dish.GetComponent<Renderer>().sharedMaterial = dishMat;

            // Dish support arm.
            var arm = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            arm.transform.SetParent(_dish.transform, false);
            arm.transform.localPosition = new Vector3(0, 0, 0.5f);
            arm.transform.localScale = new Vector3(0.08f, 0.6f, 0.08f);
            arm.transform.localRotation = Quaternion.Euler(20, 0, 0);
            Object.DestroyImmediate(arm.GetComponent<Collider>());
            arm.GetComponent<Renderer>().sharedMaterial = mastMat;

            // Sensor light.
            _sensorMat = new Material(mastMat);
            if (_sensorMat.HasProperty("_EmissionColor"))
            {
                _sensorMat.EnableKeyword("_EMISSION");
                _sensorMat.SetColor("_EmissionColor", beamColor * 2f);
            }
            var sensor = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sensor.transform.SetParent(_dish.transform, false);
            sensor.transform.localPosition = new Vector3(0, 0.2f, 0);
            sensor.transform.localScale = Vector3.one * 0.15f;
            Object.DestroyImmediate(sensor.GetComponent<Collider>());
            sensor.GetComponent<Renderer>().sharedMaterial = _sensorMat;

            // Beacon beam.
            _beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _beam.name = "BeaconBeam";
            _beam.transform.SetParent(transform, false);
            _beam.transform.localPosition = new Vector3(0, 6.5f + beamHeight * 0.5f, 0);
            _beam.transform.localScale = new Vector3(0.2f, beamHeight * 0.5f, 0.2f);
            Object.DestroyImmediate(_beam.GetComponent<Collider>());

            _beamMat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            _beamMat.color = beamColor;
            if (_beamMat.HasProperty("_BaseColor")) _beamMat.SetColor("_BaseColor", beamColor);
            _beam.GetComponent<Renderer>().sharedMaterial = _beamMat;

            // Point light at the top.
            _beaconLight = _dish.AddComponent<Light>();
            _beaconLight.type = LightType.Point;
            _beaconLight.color = beamColor;
            _beaconLight.range = 40f;
            _beaconLight.intensity = 3f;
        }
    }
}
