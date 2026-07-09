using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Items;

namespace VoxelEngine.Simulation
{
    public class HighVoltageWireTool : MonoBehaviour
    {
        public static HighVoltageWireTool Instance { get; private set; }

        public Camera playerCamera;
        public Inventory inventory;
        public float reach = 20f;
        public LayerMask stationLayer;

        private IVoltageStation _firstStation;
        private LineRenderer _previewLine;

        private void Awake()
        {
            Instance = this;
            var go = new GameObject("WirePreview");
            _previewLine = go.AddComponent<LineRenderer>();
            _previewLine.startWidth = 0.05f;
            _previewLine.endWidth = 0.05f;
            _previewLine.positionCount = 2;
            _previewLine.enabled = false;
            
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.color = Color.yellow;
            _previewLine.material = mat;
        }

        private void Update()
        {
            if (inventory == null) return;
            var stack = inventory.ActiveStack;
            if (stack.IsEmpty)
            {
                CancelConnection();
                return;
            }

            bool isHV = stack.item.itemId == "hv_wire";
            bool isLV = stack.item.itemId.EndsWith("_lv_wire");

            if (!isHV && !isLV)
            {
                CancelConnection();
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                HandleClick(isHV, isLV);
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelConnection();
            }

            UpdatePreview();
        }

        private void HandleClick(bool holdingHV, bool holdingLV)
        {
            Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            if (Physics.Raycast(ray, out RaycastHit hit, reach, stationLayer))
            {
                var station = hit.collider.GetComponentInParent<IVoltageStation>();
                if (station != null)
                {
                    bool stationIsHV = station.IsHighVoltage;
                    
                    if (holdingHV && !stationIsHV)
                    {
                        VoxelEngine.UI.BuildFeedbackHud.Show("Invalid Wire", "HV Wire only for HV stations!", null, Color.red);
                        return;
                    }
                    if (holdingLV && stationIsHV && !(station is StepUpTransformer || station is StepDownTransformer))
                    {
                         VoxelEngine.UI.BuildFeedbackHud.Show("Invalid Wire", "LV Wire only for LV stations!", null, Color.red);
                         return;
                    }

                    if (_firstStation == null)
                    {
                        _firstStation = station;
                        _previewLine.enabled = true;
                    }
                    else if (_firstStation != station)
                    {
                        float capacity = GetCurrentWireCapacity();
                        if (ConnectStations(_firstStation, station, capacity))
                        {
                            var item = inventory.ActiveStack.item;
                            inventory.container.Remove(item, 1);
                            VoxelEngine.UI.BuildFeedbackHud.Show("Wire Connected", "-1 " + item.displayName, item.icon, new Color(0.95f, 0.85f, 0.20f));
                        }
                        _firstStation = null;
                        _previewLine.enabled = false;
                    }
                }
            }
        }

        private float GetCurrentWireCapacity()
        {
            var id = inventory.ActiveStack.item.itemId;
            if (id == "hv_wire") return 1000000000f; // Infinite
            if (id == "copper_lv_wire") return 1500f;
            if (id == "gold_lv_wire") return 15000f;
            if (id == "graphite_lv_wire") return 50000f;
            return 1000f;
        }

        private bool ConnectStations(IVoltageStation a, IVoltageStation b, float capacity)
        {
            float maxReach = 200f;
            if (inventory.ActiveStack.item.itemId.EndsWith("_lv_wire")) maxReach = 15f;

            if (Vector3.Distance(a.ConnectionPoint, b.ConnectionPoint) > maxReach)
            {
                VoxelEngine.UI.BuildFeedbackHud.Show("Too Far!", $"Max reach is {maxReach}m", null, Color.red);
                return false;
            }

            a.AddConnection(b, capacity);
            b.AddConnection(a, capacity);
            return true;
        }

        private void CancelConnection()
        {
            _firstStation = null;
            if (_previewLine != null) _previewLine.enabled = false;
        }

        private void UpdatePreview()
        {
            if (_firstStation != null && _previewLine != null)
            {
                _previewLine.SetPosition(0, _firstStation.ConnectionPoint);
                
                Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
                Vector3 endPoint;
                if (Physics.Raycast(ray, out RaycastHit hit, reach))
                    endPoint = hit.point;
                else
                    endPoint = ray.GetPoint(reach);
                
                _previewLine.SetPosition(1, endPoint);
            }
        }
    }
}
