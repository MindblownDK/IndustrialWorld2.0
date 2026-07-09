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
        public ItemDefinition wireItem;

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
            if (stack.IsEmpty || stack.item.itemId != "hv_wire")
            {
                CancelConnection();
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                HandleClick();
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelConnection();
            }

            UpdatePreview();
        }

        private void HandleClick()
        {
            Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            if (Physics.Raycast(ray, out RaycastHit hit, reach, stationLayer))
            {
                var station = hit.collider.GetComponentInParent<IVoltageStation>();
                if (station != null)
                {
                    if (_firstStation == null)
                    {
                        _firstStation = station;
                        _previewLine.enabled = true;
                    }
                    else if (_firstStation != station)
                    {
                        if (ConnectStations(_firstStation, station))
                        {
                            var item = inventory.ActiveStack.item;
                            inventory.container.Remove(item, 1);
                            VoxelEngine.UI.BuildFeedbackHud.Show("HV Wire Connected", "-1 " + item.displayName, item.icon, new Color(0.95f, 0.85f, 0.20f));
                        }
                        _firstStation = null;
                        _previewLine.enabled = false;
                    }
                }
            }
        }

        private bool ConnectStations(IVoltageStation a, IVoltageStation b)
        {
            if (Vector3.Distance(a.ConnectionPoint, b.ConnectionPoint) > 200f) // Max reach
            {
                Debug.Log("Stations too far apart!");
                return false;
            }

            a.AddConnection(b);
            b.AddConnection(a);
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
