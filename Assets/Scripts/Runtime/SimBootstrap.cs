using Unity.Jobs;
using UnityEngine;

public sealed class SimBootstrap : MonoBehaviour
{
    [Header("Grid")]
    [SerializeField, Min(4)] private int width = 64;
    [SerializeField, Min(4)] private int height = 64;

    [Header("Timing")]
    [SerializeField] private float tickRate = 20f;
    [SerializeField] private float timeScale = 1f;
    [SerializeField, Range(1, 16)] private int maxTicksPerFrame = 4;
    [SerializeField] private bool paused;

    private const int MaxTickBudget = 8192;

    private SimGridData _grid;
    private SimPipeline _pipeline;
    private SimVisualizer _visualizer;
    private JobHandle _simHandle;
    private float _accumulator;

    public SimGridData Grid => _grid;
    public SimVisualizer Visualizer => _visualizer;
    public int TickIndex => _pipeline != null ? _pipeline.TickIndex : 0;
    public float TickRate => tickRate;
    public int LastTicksThisFrame { get; private set; }

    public float TimeScale
    {
        get => timeScale;
        set
        {
            float clamped = Mathf.Clamp(value, 0.01f, 10000f);
            if (Mathf.Approximately(timeScale, clamped))
            {
                return;
            }

            timeScale = clamped;
            _accumulator = 0f;
        }
    }

    public float EffectiveTickRate => Mathf.Max(0.01f, tickRate * timeScale);

    public bool Paused
    {
        get => paused;
        set => paused = value;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoBootstrap()
    {
        if (FindFirstObjectByType<SimBootstrap>() != null)
        {
            return;
        }

        var host = new GameObject("SimBootstrap");
        host.AddComponent<SimBootstrap>();
    }

    private void Awake()
    {
        timeScale = Mathf.Clamp(timeScale, 0.01f, 10000f);
        _grid = new SimGridData(width, height, ElementRegistry.BuildDefault());
        SimSceneInitializer.Apply(_grid);

        _pipeline = new SimPipeline()
            .AddPass(new ThermalConductionPass());
            // Append new independent passes here, e.g.:
            // .AddPass(new GasDiffusionPass())
            // .AddPass(new LiquidFlowPass())
            // .AddPass(new PhaseChangePass());

        _visualizer = GetComponent<SimVisualizer>();
        if (_visualizer == null)
        {
            _visualizer = gameObject.AddComponent<SimVisualizer>();
        }
        _visualizer.Initialize(_grid);

        if (GetComponent<SimDebugOverlay>() == null)
        {
            gameObject.AddComponent<SimDebugOverlay>();
        }

        SetupCamera();
    }

    private void SetupCamera()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            var cameraObject = new GameObject("SimCamera");
            cameraObject.tag = "MainCamera";
            camera = cameraObject.AddComponent<Camera>();
        }

        camera.orthographic = true;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        camera.transform.rotation = Quaternion.identity;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 100f;
        camera.orthographicSize = Mathf.Max(
            height * 0.5f,
            width * 0.5f / Mathf.Max(0.1f, camera.aspect)) * 1.02f;
    }

    private void Update()
    {
        LastTicksThisFrame = 0;

        if (paused || _pipeline == null)
        {
            return;
        }

        float dt = 1f / EffectiveTickRate;
        int tickBudget = Mathf.Clamp(Mathf.CeilToInt(maxTicksPerFrame * timeScale), 1, MaxTickBudget);
        _accumulator += Mathf.Min(Time.deltaTime, dt * tickBudget);

        while (_accumulator >= dt && LastTicksThisFrame < tickBudget)
        {
            _simHandle = _pipeline.Step(_grid, dt, _simHandle);
            _accumulator -= dt;
            LastTicksThisFrame++;
        }
    }

    private void LateUpdate()
    {
        _simHandle.Complete();
        _visualizer?.Refresh();
    }

    private void OnDestroy()
    {
        _simHandle.Complete();
        _visualizer?.Dispose();
        _grid?.Dispose();
    }

    public void Paint(int centerX, int centerY, float radius, ushort elementId, float mass, float temperature)
    {
        if (_grid == null || elementId >= _grid.ElementDefs.Length)
        {
            return;
        }

        _simHandle.Complete();

        int reach = Mathf.Max(0, Mathf.CeilToInt(radius));
        float radiusSq = radius * radius + 0.25f;

        for (int dy = -reach; dy <= reach; dy++)
        {
            for (int dx = -reach; dx <= reach; dx++)
            {
                if (dx * dx + dy * dy > radiusSq)
                {
                    continue;
                }

                int x = centerX + dx;
                int y = centerY + dy;
                if (!_grid.InBounds(x, y))
                {
                    continue;
                }

                int index = _grid.ToIndex(x, y);
                _grid.ElementIds[index] = elementId;
                _grid.Masses[index] = Mathf.Max(0f, mass);
                _grid.Temperatures[index] = temperature;
            }
        }
    }
}
