using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public sealed class SimDebugOverlay : MonoBehaviour
{
    private static readonly Rect ExpandedPanelRect = new Rect(4f, 4f, 290f, 640f);
    private static readonly Rect CollapsedButtonRect = new Rect(4f, 4f, 96f, 24f);

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
    private float _brushTemperatureC = 25f;
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

    private bool IsPointerOverUI()
    {
        Vector2 guiMouse = new Vector2(_mousePosition.x, Screen.height - _mousePosition.y);
        Rect activeRect = _showPanel ? ExpandedPanelRect : CollapsedButtonRect;
        return activeRect.Contains(guiMouse);
    }

    private void Update()
    {
        _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.1f);

        ReadMouse();
        UpdateHoverCell();

        // 鼠标位于 UI 面板上时拦截绘制，避免误刷底层网格
        if (IsPointerOverUI() || _hoverX < 0 || _bootstrap == null || _bootstrap.Grid == null)
        {
            return;
        }

        if (_leftHeld)
        {
            float tempK = ElementRegistry.CelsiusToKelvin(_brushTemperatureC);
            _bootstrap.Paint(_hoverX, _hoverY, _brushRadius, (ushort)_selectedElement, _brushMass, tempK);
        }
        else if (_rightHeld)
        {
            _bootstrap.Paint(_hoverX, _hoverY, _brushRadius, ElementIds.Vacuum, 0f, 0f);
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

        Camera cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        Vector3 world = cam.ScreenToWorldPoint(
            new Vector3(_mousePosition.x, _mousePosition.y, -cam.transform.position.z));

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
            if (GUI.Button(CollapsedButtonRect, "显示面板"))
            {
                _showPanel = true;
            }
            return;
        }

        GUILayout.BeginArea(ExpandedPanelRect, GUI.skin.box);

        GUILayout.BeginHorizontal();
        GUILayout.Label("XNotInclude - 多物理场实验");
        if (GUILayout.Button("隐藏", GUILayout.Width(48f)))
        {
            _showPanel = false;
        }
        GUILayout.EndHorizontal();

        GUILayout.Label($"FPS {_fps:0.0} | Tick {_bootstrap.TickIndex} | 本帧 {_bootstrap.LastTicksThisFrame}");
        GUILayout.Label($"单 Tick 耗时: {_bootstrap.LastTickMs:0.000} ms (dt={_bootstrap.SimDt:0.00}s)");

        SimVisualizer visualizer = _bootstrap.Visualizer;
        if (visualizer != null)
        {
            var mode = (SimOverlayMode)GUILayout.Toolbar(
                (int)visualizer.Mode,
                new[] { "温度", "材质", "质量" });
            visualizer.Mode = mode;

            if (mode == SimOverlayMode.Temperature)
            {
                GUILayout.Label($"色标 {visualizer.MinTempC:0.0}°C → {visualizer.MaxTempC:0.0}°C");
            }
        }

        if (GUILayout.Button(_bootstrap.Paused ? "继续模拟" : "暂停模拟"))
        {
            _bootstrap.Paused = !_bootstrap.Paused;
        }

        GUILayout.Space(6f);
        GUILayout.Label($"模拟速度 {_bootstrap.TimeScale:0.00}x ({_bootstrap.EffectiveTickRate:0.0} tick/s)");
        float speedLog = Mathf.Log10(Mathf.Clamp(_bootstrap.TimeScale, 0.01f, 10000f));
        float newSpeedLog = GUILayout.HorizontalSlider(speedLog, -2f, 4f);
        if (!Mathf.Approximately(newSpeedLog, speedLog))
        {
            SetTimeScale(Mathf.Pow(10f, newSpeedLog));
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("倍数", GUILayout.Width(40f));
        string newSpeedText = GUILayout.TextField(_speedText);
        if (newSpeedText != _speedText)
        {
            _speedText = newSpeedText;
            if (float.TryParse(_speedText, out float parsedSpeed))
            {
                _bootstrap.TimeScale = parsedSpeed;
            }
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        float[] presets = { 0.1f, 1f, 10f, 100f, 1000f };
        string[] presetLabels = { "0.1x", "1x", "10x", "100x", "1000x" };
        for (int i = 0; i < presets.Length; i++)
        {
            if (GUILayout.Button(presetLabels[i], GUILayout.Width(52f)))
            {
                SetTimeScale(presets[i]);
            }
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);
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

        GUILayout.Label($"半径 {_brushRadius:0.0}");
        _brushRadius = GUILayout.HorizontalSlider(_brushRadius, 0f, 6f);

        ElementDef selectedDef = ElementRegistry.GetDef((ushort)_selectedElement);
        float massMax = selectedDef.MaxMass > 0f ? selectedDef.MaxMass : 1000f;
        float massSliderMax = Mathf.Max(massMax, _brushMass);
        GUILayout.Label($"质量 {_brushMass:0.00} kg / 上限 {massMax:0.0}");
        float newMass = GUILayout.HorizontalSlider(_brushMass, 0f, massSliderMax);
        if (!Mathf.Approximately(newMass, _brushMass))
        {
            _brushMass = newMass;
            _brushMassText = _brushMass.ToString("0.000");
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("质量 kg", GUILayout.Width(64f));
        string newMassText = GUILayout.TextField(_brushMassText);
        if (newMassText != _brushMassText)
        {
            _brushMassText = newMassText;
            if (float.TryParse(_brushMassText, out float parsedMass))
            {
                _brushMass = Mathf.Max(0f, parsedMass);
            }
        }
        if (GUILayout.Button("默认", GUILayout.Width(48f)))
        {
            ResetBrushMassToDefault();
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("温度 °C", GUILayout.Width(64f));
        string newTempText = GUILayout.TextField(_brushTemperatureText);
        if (newTempText != _brushTemperatureText)
        {
            _brushTemperatureText = newTempText;
            if (float.TryParse(_brushTemperatureText, out float parsedTemp))
            {
                _brushTemperatureC = Mathf.Clamp(parsedTemp, -272.15f, 9726.85f);
            }
        }
        GUILayout.EndHorizontal();
        GUILayout.Label("左键绘制 / 右键擦除(真空)");

        GUILayout.Space(6f);
        SimGridData grid = _bootstrap.Grid;
        if (_hoverX >= 0)
        {
            int index = grid.ToIndex(_hoverX, _hoverY);
            ushort id = grid.ElementIds[index];
            ElementDef def = id < grid.ElementDefs.Length ? grid.ElementDefs[id] : default;
            GUILayout.Label($"格子 ({_hoverX}, {_hoverY}) {ElementRegistry.GetDisplayName(id)}");
            if (id == ElementIds.Vacuum)
            {
                GUILayout.Label("T = 真空 (0 K)  M = 0.000 kg");
            }
            else
            {
                float tempK = grid.Temperatures[index];
                float tempC = ElementRegistry.KelvinToCelsius(tempK);
                GUILayout.Label($"T = {tempC:0.00}°C ({tempK:0.0}K)  M = {grid.Masses[index]:0.00} kg");
            }
            GUILayout.Label($"SHC = {def.SHC:0.000}  TC = {def.TC:0.000}  Ins = {def.IsInsulator}");
        }
        else
        {
            GUILayout.Label("鼠标移入网格查看读数");
        }

        GUILayout.EndArea();
    }
}