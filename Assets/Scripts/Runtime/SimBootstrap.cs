using System.Diagnostics;
using Unity.Jobs;
using UnityEngine;

public sealed class SimBootstrap : MonoBehaviour
{
    [Header("Grid")]
    [SerializeField, Min(4)] private int width = 64;
    [SerializeField, Min(4)] private int height = 64;

    [Header("Timing")]
    [SerializeField] private float tickRate = 5f;        // 对齐《缺氧》原版 5Hz (simDt = 0.2s)
    [SerializeField] private float timeScale = 1f;
    [SerializeField, Range(1, 64)] private int maxTicksPerFrame = 8;
    [SerializeField] private bool paused;

    private const int MaxTickBudget = 4096;

    private SimGridData _grid;
    private SimPipeline _pipeline;
    private SimVisualizer _visualizer;
    private JobHandle _simHandle;
    private float _accumulator;
    private readonly Stopwatch _sw = new Stopwatch();

    public SimGridData Grid => _grid;
    public SimVisualizer Visualizer => _visualizer;
    public int TickIndex => _pipeline != null ? _pipeline.TickIndex : 0;
    public float TickRate => tickRate;
    public float SimDt => 1f / Mathf.Max(0.01f, tickRate);
    public int LastTicksThisFrame { get; private set; }
    public double LastTickMs { get; private set; }

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
            // 后续扩展直接追加：
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
        Camera cam = Camera.main;
        if (cam == null)
        {
            var cameraObject = new GameObject("SimCamera");
            cameraObject.tag = "MainCamera";
            cam = cameraObject.AddComponent<Camera>();
        }

        cam.orthographic = true;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        cam.transform.rotation = Quaternion.identity;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 100f;
        cam.orthographicSize = Mathf.Max(
            height * 0.5f,
            width * 0.5f / Mathf.Max(0.1f, cam.aspect)) * 1.02f;
    }

    private void Update()
    {
        LastTicksThisFrame = 0;

        if (paused || _pipeline == null)
        {
            return;
        }

        // 固定物理步长 simDt，通过 timeScale 加速时间累积，保证真变速与确定性
        float simDt = SimDt;
        int tickBudget = Mathf.Clamp(Mathf.CeilToInt(maxTicksPerFrame * timeScale), 1, MaxTickBudget);
        _accumulator += Mathf.Min(Time.deltaTime * timeScale, simDt * tickBudget);

        if (_accumulator >= simDt)
        {
            _sw.Restart();
            while (_accumulator >= simDt && LastTicksThisFrame < tickBudget)
            {
                _simHandle = _pipeline.Step(_grid, simDt, _simHandle);
                _accumulator -= simDt;
                LastTicksThisFrame++;
            }
        }
    }

    private void LateUpdate()
    {
        _simHandle.Complete();
        if (LastTicksThisFrame > 0)
        {
            _sw.Stop();
            LastTickMs = _sw.Elapsed.TotalMilliseconds / LastTicksThisFrame;
        }

        _visualizer?.Refresh();
    }

    private void OnDestroy()
    {
        _simHandle.Complete();
        _visualizer?.Dispose();
        _grid?.Dispose();
    }

    /// <summary>
    /// 绘制材质，temperatureK 单位为开尔文 (K)。真空会自动归零质量与温度。
    /// </summary>
    public void Paint(int centerX, int centerY, float radius, ushort elementId, float mass, float temperatureK)
    {
        if (_grid == null || elementId >= _grid.ElementDefs.Length)
        {
            return;
        }

        _simHandle.Complete();

        bool isVacuum = elementId == ElementIds.Vacuum;
        float finalMass = isVacuum ? 0f : Mathf.Max(0f, mass);
        float finalTempK = isVacuum ? 0f : Mathf.Clamp(temperatureK, 1f, 10000f);

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
                _grid.Masses[index] = finalMass;
                _grid.Temperatures[index] = finalTempK;
            }
        }
    }
}