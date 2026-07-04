// Assets/Scripts/VoxelEngine/GridSystem/StationaryRadarBeacon.cs
//
// Stationary Radar Beacon — a world-placed tall tower with a rotating radar
// dish on top + a visible beacon beam. Looks like a coastal radar station.
// Toggle on/off, draws 10W.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class StationaryRadarBeacon : MonoBehaviour
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

        private void Awake()
        {
            CreateVisuals();
        }

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
            var sensorMat = new Material(mastMat);
            if (sensorMat.HasProperty("_EmissionColor"))
            {
                sensorMat.EnableKeyword("_EMISSION");
                sensorMat.SetColor("_EmissionColor", beamColor * 2f);
            }
            var sensor = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sensor.transform.SetParent(_dish.transform, false);
            sensor.transform.localPosition = new Vector3(0, 0.2f, 0);
            sensor.transform.localScale = Vector3.one * 0.15f;
            Object.DestroyImmediate(sensor.GetComponent<Collider>());
            sensor.GetComponent<Renderer>().sharedMaterial = sensorMat;

            // Beacon beam.
            _beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _beam.name = "BeaconBeam";
            _beam.transform.SetParent(transform, false);
            _beam.transform.localPosition = new Vector3(0, 6.5f + beamHeight * 0.5f, 0);
            _beam.transform.localScale = new Vector3(0.2f, beamHeight * 0.5f, 0.2f);
            Object.DestroyImmediate(_beam.GetComponent<Collider>());

            var beamMat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            beamMat.color = beamColor;
            _beam.GetComponent<Renderer>().sharedMaterial = beamMat;

            // Point light at the top.
            _beaconLight = _dish.AddComponent<Light>();
            _beaconLight.type = LightType.Point;
            _beaconLight.color = beamColor;
            _beaconLight.range = 40f;
            _beaconLight.intensity = 3f;
        }
    }
}
