// Assets/Scripts/VoxelEngine/Power/Wind/WindmillAssembly.cs
// Handles multi-stage assembly for windmills. Stationary, non-grid placed large structures.
// Supports Standard (tower->nacelle->internals->hub->3x wings) and Helix (gen base + rotor).
// Player interaction: open nacelle, climb (for large), install components via payload or direct calls.
// MAX EFFORT: beautiful, interactive, stateful, with interior access for largest.

using System;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;
using VoxelEngine.Power;

namespace VoxelEngine.Power.Wind
{
    [RequireComponent(typeof(PlacedBlock))]
    public class WindmillAssembly : MonoBehaviour, IPlacedBlockPayloadReceiver
    {
        public enum AssemblyStage
        {
            Placed,              // Tower / Base placed
            NacelleInstalled,    // Standard only
            InternalsComplete,   // Gearbox + Generator
            HubInstalled,
            FullyAssembled
        }

        public enum HelixStage
        {
            BasePlaced,
            WingsInstalled
        }

        [Header("Windmill Config")]
        public WindmillDefinition definition;
        public WindmillDefinition.WindmillType windmillType = WindmillDefinition.WindmillType.Standard; // Standard or Helix

        [Header("Current State")]
        public AssemblyStage standardStage = AssemblyStage.Placed;
        public HelixStage helixStage = HelixStage.BasePlaced;

        public bool hasGearbox = false;
        public bool hasGenerator = false;
        public int wingsInstalled = 0;
        public bool nacelleOpen = false;

        [Header("Visual Parts (assign in prefab or generated)")]
        public GameObject towerRoot;
        public GameObject nacelle;
        public GameObject nacelleRoof;
        public GameObject hub;
        public GameObject[] blades = new GameObject[3];
        public GameObject helixRotor;
        public GameObject monopoleBase; // if water placed

        [Header("Interior (Large Standard)")]
        public Transform ladderStart;
        public Transform nacelleInteriorFloor;
        public GameObject interiorAccessTrigger; // for player to enter
        public bool playerInsideNacelle = false;

        [Header("Runtime")]
        public float currentEfficiency = 1f;
        private WindmillRotor rotor;
        private PowerGenerator powerGenRef;

        private void Awake()
        {
            powerGenRef = GetComponent<PowerGenerator>();
            rotor = GetComponentInChildren<WindmillRotor>();
            if (rotor == null) rotor = gameObject.AddComponent<WindmillRotor>();
        }

        private void Start()
        {
            // Ensure stationary (no grid requirement)
            var pb = GetComponent<PlacedBlock>();
            if (pb != null) pb.onGrid = false;

            if (definition == null)
            {
                // Fallback default
                definition = ScriptableObject.CreateInstance<WindmillDefinition>();
                definition.maxPowerWatts = 2500000f;
            }

            UpdateVisuals();
        }

        private void Update()
        {
            if (IsFullyAssembled())
            {
                UpdatePowerAndRotation();
            }
            else if (powerGenRef != null)
            {
                powerGenRef.wattsPerSecond = 0;
            }
        }

        private void UpdatePowerAndRotation()
        {
            if (WindSystem.Instance == null || powerGenRef == null) return;

            float windSpeed = WindSystem.Instance.GetWindSpeed();
            float height = transform.position.y;
            bool obstructed = WindSystem.Instance.IsObstructed(transform.position + Vector3.up * 6f);

            float targetPower = definition.GetEffectiveMaxPower(windSpeed, height, obstructed);
            powerGenRef.wattsPerSecond = targetPower * currentEfficiency;

            // Spin rotor
            if (rotor != null)
            {
                float rpm = Mathf.Clamp(windSpeed * 6.5f * (obstructed ? 0.6f : 1f), 2f, 38f);
                rotor.SetTargetRPM(rpm);
            }
        }

        public bool IsFullyAssembled()
        {
            if (windmillType == WindmillDefinition.WindmillType.Standard)
                return standardStage == AssemblyStage.FullyAssembled;
            else
                return helixStage == HelixStage.WingsInstalled;
        }

        public void InstallPart(string partType)
        {
            if (windmillType == WindmillDefinition.WindmillType.Standard)
            {
                switch (partType.ToLower())
                {
                    case "nacelle":
                    case "nacellekit":
                        if (standardStage == AssemblyStage.Placed)
                            standardStage = AssemblyStage.NacelleInstalled;
                        UpdateVisuals();
                        break;

                    case "gearbox":
                        if (standardStage == AssemblyStage.NacelleInstalled)
                            hasGearbox = true;
                        TryAdvanceInternals();
                        break;

                    case "generator":
                        if (standardStage == AssemblyStage.NacelleInstalled)
                            hasGenerator = true;
                        TryAdvanceInternals();
                        break;

                    case "hub":
                        if (standardStage == AssemblyStage.InternalsComplete)
                            standardStage = AssemblyStage.HubInstalled;
                        UpdateVisuals();
                        break;

                    case "wing":
                    case "blade":
                        if (standardStage == AssemblyStage.HubInstalled && wingsInstalled < 3)
                        {
                            wingsInstalled++;
                            if (wingsInstalled >= 3)
                                standardStage = AssemblyStage.FullyAssembled;
                        }
                        UpdateVisuals();
                        break;
                }
            }
            else // Helix
            {
                if (partType.ToLower().Contains("generator"))
                {
                    helixStage = HelixStage.BasePlaced; // already placed
                }
                else if (partType.ToLower().Contains("wing") || partType.ToLower().Contains("rotor"))
                {
                    helixStage = HelixStage.WingsInstalled;
                    UpdateVisuals();
                }
            }
        }

        private void TryAdvanceInternals()
        {
            if (hasGearbox && hasGenerator && standardStage == AssemblyStage.NacelleInstalled)
            {
                standardStage = AssemblyStage.InternalsComplete;
                UpdateVisuals();
            }
        }

        public void ToggleNacelleRoof()
        {
            nacelleOpen = !nacelleOpen;
            if (nacelleRoof != null)
            {
                Vector3 target = nacelleOpen ? new Vector3(0, 3.2f, 0) : Vector3.zero;
                nacelleRoof.transform.localPosition = Vector3.Lerp(nacelleRoof.transform.localPosition, target, 0.6f);
            }
        }

        // Called from player interaction (large windmill)
        public void TryEnterNacelle(Transform player)
        {
            if (!definition.hasClimbableInterior || !IsFullyAssembled()) return;

            if (ladderStart != null)
            {
                // Simple teleport to nacelle (in real would use ladder climb animation + physics)
                player.position = ladderStart.position + Vector3.up * 2.5f;
                playerInsideNacelle = true;
                // Could open UI for "inside nacelle panel" or allow placing generator parts here
            }
        }

        public void ExitNacelle(Transform player)
        {
            if (player != null && nacelleInteriorFloor != null)
            {
                player.position = transform.position + Vector3.down * 12f + Vector3.forward * 3f; // descend
            }
            playerInsideNacelle = false;
        }

        private void UpdateVisuals()
        {
            if (nacelle != null) nacelle.SetActive(standardStage >= AssemblyStage.NacelleInstalled || windmillType == WindmillType.HelixVertical);
            if (hub != null) hub.SetActive(standardStage >= AssemblyStage.HubInstalled);
            if (nacelleRoof != null) nacelleRoof.SetActive(standardStage >= AssemblyStage.NacelleInstalled);

            for (int i = 0; i < blades.Length; i++)
            {
                if (blades[i] != null)
                    blades[i].SetActive(wingsInstalled > i || (windmillType == WindmillType.HelixVertical && helixStage == HelixStage.WingsInstalled));
            }

            if (helixRotor != null)
                helixRotor.SetActive(helixStage >= HelixStage.WingsInstalled);

            // Adjust efficiency for mismatched helix sizes (handled in HelixWindmill but exposed here)
            if (windmillType == WindmillType.HelixVertical)
            {
                // Will be overridden by specific Helix controller if needed
                currentEfficiency = 1f;
            }
        }

        // IPlacedBlockPayloadReceiver — allows installing parts by using items on the placed windmill
        public void ApplyPlacedPayload(ItemStack payload)
        {
            if (payload.IsEmpty || payload.item == null) return;

            string id = payload.item.itemId.ToLower();
            bool consumed = false;

            if (id.Contains("nacelle") || id.Contains("wind_nacelle"))
            {
                InstallPart("nacelle");
                consumed = true;
            }
            else if (id.Contains("gearbox"))
            {
                InstallPart("gearbox");
                consumed = true;
            }
            else if (id.Contains("generator") && !id.Contains("helix"))
            {
                InstallPart("generator");
                consumed = true;
            }
            else if (id.Contains("hub"))
            {
                InstallPart("hub");
                consumed = true;
            }
            else if (id.Contains("blade") || id.Contains("wing"))
            {
                InstallPart("wing");
                consumed = true;
            }
            else if (id.Contains("helix") && id.Contains("generator"))
            {
                InstallPart("helixgenerator");
                consumed = true;
            }
            else if (id.Contains("helix") && (id.Contains("wing") || id.Contains("rotor")))
            {
                InstallPart("helixwing");
                consumed = true;
            }

            if (consumed && payload.count > 0)
            {
                // Note: in full game, inventory would consume. Here we signal via log for prototype.
                Debug.Log($"[WindmillAssembly] Installed {payload.item.displayName} into {name}");
            }
        }

        public void SetDefinition(WindmillDefinition def)
        {
            definition = def;
            if (def != null)
            {
                windmillType = def.type == WindmillDefinition.WindmillType.Standard ? WindmillType.Standard : WindmillType.HelixVertical;
            }
        }
    }

    // Simple rotor spinner attached to blades/hub
    [RequireComponent(typeof(Transform))]
    public class WindmillRotor : MonoBehaviour
    {
        public float targetRPM = 12f;
        private float _currentRPM;
        public Transform[] bladeTransforms; // assign children

        private void Awake()
        {
            if (bladeTransforms == null || bladeTransforms.Length == 0)
                bladeTransforms = GetComponentsInChildren<Transform>();
        }

        private void Update()
        {
            _currentRPM = Mathf.Lerp(_currentRPM, targetRPM, Time.deltaTime * 1.8f);
            float degPerSec = _currentRPM * 6f;
            transform.Rotate(Vector3.forward, degPerSec * Time.deltaTime, Space.Self);

            // Optional: slight blade pitch variation for beauty
            if (bladeTransforms != null)
            {
                foreach (var t in bladeTransforms)
                {
                    if (t != transform && t.name.ToLower().Contains("blade"))
                        t.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(Time.time * 0.3f) * 1.5f);
                }
            }
        }

        public void SetTargetRPM(float rpm)
        {
            targetRPM = rpm;
        }
    }
}
