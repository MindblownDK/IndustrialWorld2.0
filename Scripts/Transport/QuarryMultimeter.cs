// Assets/Scripts/VoxelEngine/Transport/QuarryMultimeter.cs
//
// 17.27.0-dev - THE QUARRY'S FRONT METER.
//
// The drill used to be a grey box that told the player nothing: whether it had power was
// only visible inside its machine panel, and "NO POWER" never said which of the two
// problems it was - no cable at all, or a network that cannot carry the load. This is a
// real instrument cluster on the machine's REAR face - the one face the player can stand
// in front of, because the pit is dug on the other side: a printed readout (status, live
// draw, maximum draw, current depth), a needle that swings across a dial as the load
// rises, a power socket and a single lamp that goes amber (no cable), red (cable, no
// supply) or green (served).
//
// Everything it prints comes from the Quarry itself, so the meter, the machine panel and
// the machine's behaviour can never disagree. It refreshes a few times a second - a meter
// is not a video display - and it does nothing at all on a placement ghost.
using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Transport
{
    [DisallowMultipleComponent]
    public class QuarryMultimeter : MonoBehaviour
    {
        [Tooltip("TextMesh that prints the readout. Wired by Tools > Voxel Engine > Voxel Engine Setup.")]
        public TextMesh screen;

        [Tooltip("Dial needle pivot. Swings from needleMinAngle (no load) to needleMaxAngle (maximum draw).")]
        public Transform needle;
        public float needleMinAngle = -52f;
        public float needleMaxAngle = 52f;

        [Tooltip("Lamp that reports the power state: amber = no cable, red = no supply, green = served.")]
        public Renderer stateLamp;

        [Tooltip("Seconds between readout rewrites.")]
        [Min(0.05f)] public float refreshInterval = 0.2f;

        private Quarry _quarry;
        private PowerConsumer _power;
        private Material _lampUnwired, _lampStarved, _lampServed;
        private string _printed;
        private float _nextRefresh;
        private float _needleFraction = -1f;

        private void Awake()
        {
            _quarry = GetComponent<Quarry>();
            _power = GetComponent<PowerConsumer>();
            // The machine's labels are authored in the prefab, which cannot carry the
            // depth-tested font material (it is made at runtime), so they are swapped in
            // here: without this the stock font shader prints the readout and the name
            // plate straight through the hull and through terrain.
            VoxelEngine.Rendering.WorldTextMaterial.ApplyToHierarchy(gameObject);
        }

        private void Update()
        {
            if (_quarry == null) return;
            // A placement preview must not run instruments; the ghost carries this script
            // for one frame before BuildSystem strips it.
            if (VoxelEngine.Building.BuildSystem.IsCreatingGhost) return;

            float maxDraw = _quarry.MaxPowerDrawW;
            float liveDraw = _quarry.LivePowerDrawW;
            float fraction = maxDraw > 0.001f ? Mathf.Clamp01(liveDraw / maxDraw) : 0f;

            // The needle eases toward its reading so a phase change reads as a sweep
            // rather than a jump.
            if (_needleFraction < 0f) _needleFraction = fraction;
            _needleFraction = Mathf.MoveTowards(_needleFraction, fraction,
                Mathf.Max(0.25f, Time.deltaTime * 1.6f));
            if (needle != null)
                needle.localRotation = Quaternion.Euler(0f, 0f,
                    Mathf.Lerp(needleMinAngle, needleMaxAngle, _needleFraction));

            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + Mathf.Max(0.05f, refreshInterval);

            var state = _quarry.PowerState;
            string text =
                "QUARRY DRILL  " + _quarry.StatusLabel + "\n" +
                $"DRAW   {liveDraw:0} / {maxDraw:0} W\n" +
                $"DEPTH  {_quarry.CurrentDepth} / {_quarry.MaxDepth} m";

            if (screen != null && !string.Equals(text, _printed))
            {
                screen.text = text;
                _printed = text;
            }
            if (screen != null)
                screen.color = state == QuarryPowerState.Served
                    ? new Color(0.82f, 1f, 0.88f)
                    : new Color(1f, 0.72f, 0.62f);

            if (stateLamp != null)
            {
                switch (state)
                {
                    case QuarryPowerState.Unwired:
                        if (_lampUnwired == null) _lampUnwired = MakeLamp(new Color(1f, 0.62f, 0.10f), "QuarryLampUnwired");
                        stateLamp.sharedMaterial = _lampUnwired;
                        break;
                    case QuarryPowerState.Starved:
                        if (_lampStarved == null) _lampStarved = MakeLamp(new Color(1f, 0.14f, 0.10f), "QuarryLampStarved");
                        stateLamp.sharedMaterial = _lampStarved;
                        break;
                    default:
                        if (_lampServed == null) _lampServed = MakeLamp(new Color(0.30f, 1f, 0.35f), "QuarryLampServed");
                        stateLamp.sharedMaterial = _lampServed;
                        break;
                }
            }
        }

        /// <summary>A lamp material that glows on its own: lit base colour plus emission,
        /// so the state is readable from the machine's face at night as well as by day.</summary>
        private static Material MakeLamp(Color color, string name)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = name, hideFlags = HideFlags.DontSave };
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * 2.2f);
            }
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return mat;
        }
    }
}
