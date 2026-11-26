using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public sealed class SimDebugOverlay : MonoBehaviour
{
    private SimBootstrap _bootstrap;
    private Vector2 _mousePosition;
    private bool _leftHeld;
    private bool _rightHeld;
    private int _hoverX = -1;
    private int _hoverY = -1;
    private int _selectedElement = ElementIds.Water;
    private string _speedText = "1.000";
    private float _brushRadius = 1.5f;
    private string _brushMassText = "1000.000";
    private float _brushMass = 1000f;
    private string _brushTemperatureText = "25.000";
    private float _brushTemperature = 25f;
    private bool _showPanel = true;
    private float _fps = 60f;

    private void Awake()
    {
        _bootstrap = GetComponent<SimBootstrap>();
        _speedText = _bootstrap.TimeScale.ToString("0.000");
        ResetBrushMassToDefault();
    }

    private void SetTimeScale(float value)
    {
        _bootstrap.TimeScale = value;
        _speedText = _bootstrap.TimeScale.ToString("0.000");
    }

    private void ResetBrushMassToDefault()
    {
        _brushMass = Mathf.Max(0f, ElementRegistry.GetDef((ushort)_selectedElement).DefaultMass);
        _brushMassText = _brushMass.ToString("0.000");
    }

    private void Update()
    {
        _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.1f);

        ReadMouse();
        UpdateHoverCell();

        if (_hoverX < 0 || _bootstrap == null || _bootstrap.Grid == null)
        {
            return;
        }

        if (_leftHeld)
        {
            _bootstrap.Paint(_hoverX, _hoverY, _brushRadius, (ushort)_selectedElement, _brushMass, _brushTemperature);
        }
        else if (_rightHeld)
        {
            _bootstrap.Paint(_hoverX, _hoverY, _brushRadius, ElementIds.Vacuum, 0f, 20f);
        }
    }

    private void ReadMouse()
    {
#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            _leftHeld = false;
            _rightHeld = false;
            return;
        }

        _mousePosition = mouse.position.ReadValue();
        _leftHeld = mouse.leftButton.isPressed;
        _rightHeld = mouse.rightButton.isPressed;
#else
        _mousePosition = Input.mousePosition;
        _leftHeld = Input.GetMouseButton(0);
        _rightHeld = Input.GetMouseButton(1);
#endif
    }

    private void UpdateHoverCell()
    {
        _hoverX = -1;
        _hoverY = -1;

        if (_bootstrap == null || _bootstrap.Grid == null)
        {
            return;
        }

        Camera camera = Camera.main;
        if (camera == null)
        {
            return;
        }

        Vector3 world = camera.ScreenToWorldPoint(
            new Vector3(_mousePosition.x, _mousePosition.y, -camera.transform.position.z));

        SimGridData grid = _bootstrap.Grid;
        int x = Mathf.FloorToInt(world.x + grid.Width * 0.5f);
        int y = Mathf.FloorToInt(world.y + grid.Height * 0.5f);

        if (grid.InBounds(x, y))
        {
            _hoverX = x;
            _hoverY = y;
        }
    }

    private void OnGUI()
    {
        if (_bootstrap == null || _bootstrap.Grid == null)
        {
            return;
        }

        if (!_showPanel)
        {
            if (GUI.Button(new Rect(4f, 4f, 96f, 24f), "显示面板"))
            {
                _showPanel = true;
            }
            return;
        }

        GUILayout.BeginArea(new Rect(4f, 4f, 280f, 620f), GUI.skin.box);
        GUILayout.Label("XNotInclude - 多物理场实验");
        GUILayout.Label($"FPS {_fps:0.000} | Tick {_bootstrap.TickIndex} | 本帧 {_bootstrap.LastTicksThisFrame}");

        SimVisualizer visualizer = _bootstrap.Visualizer;
        if (visualizer != null)
        {
            var mode = (SimOverlayMode)GUILayout.Toolbar(
                (int)visualizer.Mode,
                new[] { "温度", "材质", "质量" });
            visualizer.Mode = mode;

            if (mode == SimOverlayMode.Temperature)
            {
                GUILayout.Label($"色标 {visualizer.MinTempC:0.000}°C → {visualizer.MaxTempC:0.000}°C");
            }
        }

        if (GUILayout.Button(_bootstrap.Paused ? "继续模拟" : "暂停模拟"))
        {
            _bootstrap.Paused = !_bootstrap.Paused;
        }

        GUILayout.Space(8f);
        GUILayout.Label($"模拟速度 {_bootstrap.TimeScale:0.000}x ({_bootstrap.EffectiveTickRate:0.000} tick/s)");
        float speedLog = Mathf.Log10(Mathf.Clamp(_bootstrap.TimeScale, 0.01f, 10000f));
        float newSpeedLog = GUILayout.HorizontalSlider(speedLog, -2f, 4f);
        if (!Mathf.Approximately(newSpeedLog, speedLog))
        {
            SetTimeScale(Mathf.Pow(10f, newSpeedLog));
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("倍数", GUILayout.Width(40f));
        _speedText = GUILayout.TextField(_speedText);
        GUILayout.EndHorizontal();
        if (float.TryParse(_speedText, out float parsedSpeed))
        {
            _bootstrap.TimeScale = parsedSpeed;
        }

        GUILayout.BeginHorizontal();
        float[] presets = { 0.01f, 1f, 100f, 1000f, 10000f };
        string[] presetLabels = { "0.01x", "1x", "100x", "1000x", "10000x" };
        for (int i = 0; i < presets.Length; i++)
        {
            if (GUILayout.Button(presetLabels[i], GUILayout.Width(52f)))
            {
                SetTimeScale(presets[i]);
            }
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(8f);
        GUILayout.Label("画刷材质");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("<", GUILayout.Width(28f)))
        {
            _selectedElement = (_selectedElement - 1 + ElementRegistry.DisplayNames.Length) % ElementRegistry.DisplayNames.Length;
            ResetBrushMassToDefault();
        }
        GUILayout.Label(ElementRegistry.GetDisplayName((ushort)_selectedElement), GUILayout.Width(110f));
        if (GUILayout.Button(">", GUILayout.Width(28f)))
        {
            _selectedElement = (_selectedElement + 1) % ElementRegistry.DisplayNames.Length;
            ResetBrushMassToDefault();
        }
        GUILayout.EndHorizontal();
        GUILayout.Label($"半径 {_brushRadius:0.000}");
        _brushRadius = GUILayout.HorizontalSlider(_brushRadius, 0f, 6f);

        ElementDef selectedDef = ElementRegistry.GetDef((ushort)_selectedElement);
        float massMax = selectedDef.MaxMass > 0f ? selectedDef.MaxMass : 1000f;
        float massSliderMax = Mathf.Max(massMax, _brushMass);
        GUILayout.Label($"质量 {_brushMass:0.000} kg / 材质上限 {massMax:0.000}");
        _brushMass = GUILayout.HorizontalSlider(_brushMass, 0f, massSliderMax);
        GUILayout.BeginHorizontal();
        GUILayout.Label("质量 kg", GUILayout.Width(64f));
        _brushMassText = GUILayout.TextField(_brushMassText);
        if (GUILayout.Button("默认", GUILayout.Width(48f)))
        {
            ResetBrushMassToDefault();
        }
        GUILayout.EndHorizontal();
        if (float.TryParse(_brushMassText, out float parsedMass))
        {
            _brushMass = Mathf.Max(0f, parsedMass);
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("温度 °C", GUILayout.Width(64f));
        _brushTemperatureText = GUILayout.TextField(_brushTemperatureText);
        GUILayout.EndHorizontal();
        if (float.TryParse(_brushTemperatureText, out float parsed))
        {
            _brushTemperature = parsed;
        }
        GUILayout.Label("左键绘制 / 右键擦除");

        GUILayout.Space(8f);
        SimGridData grid = _bootstrap.Grid;
        if (_hoverX >= 0)
        {
            int index = grid.ToIndex(_hoverX, _hoverY);
            ushort id = grid.ElementIds[index];
            ElementDef def = id < grid.ElementDefs.Length ? grid.ElementDefs[id] : default;
            GUILayout.Label($"格子 ({_hoverX}, {_hoverY}) {ElementRegistry.GetDisplayName(id)}");
            GUILayout.Label($"T = {grid.Temperatures[index]:0.000}°C  M = {grid.Masses[index]:0.000} kg");
            GUILayout.Label($"SHC = {def.SHC:0.000}  TC = {def.TC:0.000}");
        }
        else
        {
            GUILayout.Label("鼠标移入网格查看读数");
        }

        GUILayout.EndArea();
    }
}
