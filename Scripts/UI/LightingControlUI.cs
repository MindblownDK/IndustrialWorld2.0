// Assets/Scripts/VoxelEngine/UI/LightingControlUI.cs
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Power;

namespace VoxelEngine.UI
{
    public class LightingControlUI : MonoBehaviour
    {
        private UIDocument _doc;
        private VisualElement _root;
        private Label _powerLabel;
        private Toggle _manualToggle;
        private Toggle _autoToggle;
        private Slider _intensitySlider;
        private Slider _rSlider;
        private Slider _gSlider;
        private Slider _bSlider;
        private VisualElement _container;

        private VoxelLightController _currentLight;

        private void Awake()
        {
            _doc = GetComponent<UIDocument>();
            _root = _doc.rootVisualElement;
            
            // Create container (initially hidden)
            _container = new VisualElement();
            _container.style.position = Position.Absolute;
            _container.style.right = 20;
            _container.style.top = 100;
            _container.style.width = 250;
            
            _container.style.paddingLeft = 15;
            _container.style.paddingRight = 15;
            _container.style.paddingTop = 15;
            _container.style.paddingBottom = 15;
            
            _container.style.backgroundColor = new Color(0.1f, 0.1f, 0.12f, 0.9f);
            _container.style.borderTopWidth = 1;
            _container.style.borderBottomWidth = 1;
            _container.style.borderLeftWidth = 1;
            _container.style.borderRightWidth = 1;
            _container.style.borderTopColor = Color.gray;
            _container.style.borderBottomColor = Color.gray;
            _container.style.borderLeftColor = Color.gray;
            _container.style.borderRightColor = Color.gray;
            
            _container.style.borderTopLeftRadius = 10;
            _container.style.borderTopRightRadius = 10;
            _container.style.borderBottomLeftRadius = 10;
            _container.style.borderBottomRightRadius = 10;
            
            _container.style.display = DisplayStyle.None;
            
            _root.Add(_container);
            BuildUI();
        }

        private void Start()
        {
            if (LightingManager.Instance != null)
            {
                LightingManager.Instance.OnLightSelected += ShowUI;
                LightingManager.Instance.OnLightDeselected += HideUI;
            }
        }

        private void BuildUI()
        {
            var title = new Label("Lighting Control");
            title.style.fontSize = 18;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 10;
            title.style.color = Color.white;
            _container.Add(title);

            _powerLabel = new Label("Power Usage: 0 W");
            _powerLabel.style.color = new Color(0.8f, 0.8f, 0.8f);
            _powerLabel.style.marginBottom = 15;
            _container.Add(_powerLabel);

            _manualToggle = new Toggle();
            _manualToggle.text = "Manual Power On";
            _manualToggle.RegisterValueChangedCallback(evt => {
                if (_currentLight != null) _currentLight.isManuallyOn = evt.newValue;
            });
            _container.Add(_manualToggle);

            _autoToggle = new Toggle();
            _autoToggle.text = "Auto Night Mode";
            _autoToggle.RegisterValueChangedCallback(evt => {
                if (_currentLight != null) _currentLight.autoNightMode = evt.newValue;
            });
            _container.Add(_autoToggle);

            var intensityLabel = new Label("Intensity");
            intensityLabel.style.color = Color.white;
            _container.Add(intensityLabel);
            
            _intensitySlider = new Slider();
            _intensitySlider.lowValue = 0;
            _intensitySlider.highValue = 100;
            _intensitySlider.value = 10;
            _intensitySlider.RegisterValueChangedCallback(evt => {
                if (_currentLight != null) _currentLight.intensity = evt.newValue;
            });
            _container.Add(_intensitySlider);

            var colorLabel = new Label("Color (RGB)");
            colorLabel.style.color = Color.white;
            _container.Add(colorLabel);
            
            _rSlider = CreateColorSlider("R", Color.red);
            _gSlider = CreateColorSlider("G", Color.green);
            _bSlider = CreateColorSlider("B", Color.blue);
            // Note: CreateColorSlider already adds to _container

            var closeBtn = new Button(() => LightingManager.Instance.DeselectLight());
            closeBtn.text = "Close";
            closeBtn.style.marginTop = 15;
            _container.Add(closeBtn);
        }

        private Slider CreateColorSlider(string labelText, Color accent)
        {
            var box = new VisualElement();
            box.style.flexDirection = FlexDirection.Row;
            box.style.alignItems = Align.Center;
            box.style.marginBottom = 5;

            var lbl = new Label(labelText);
            lbl.style.width = 30;
            lbl.style.color = accent;
            
            var s = new Slider();
            s.lowValue = 0;
            s.highValue = 1;
            s.value = 1;
            s.style.flexGrow = 1;
            s.RegisterValueChangedCallback(evt => UpdateColor());
            
            box.Add(lbl);
            box.Add(s);
            _container.Add(box);
            return s;
        }

        private void UpdateColor()
        {
            if (_currentLight == null) return;
            _currentLight.lightColor = new Color(_rSlider.value, _gSlider.value, _bSlider.value);
        }

        private void ShowUI(VoxelLightController light)
        {
            _currentLight = light;
            _container.style.display = DisplayStyle.Flex;
            
            // Update values
            _manualToggle.value = light.isManuallyOn;
            _autoToggle.value = light.autoNightMode;
            _intensitySlider.value = light.intensity;
            
            _rSlider.value = light.lightColor.r;
            _gSlider.value = light.lightColor.g;
            _bSlider.value = light.lightColor.b;
            
            _powerLabel.text = $"Power Usage: {VoxelEngine.Power.PowerFormatter.FormatWatts(light.GetPowerUsage())}";
        }

        private void HideUI()
        {
            _currentLight = null;
            _container.style.display = DisplayStyle.None;
        }
    }
}
