$ErrorActionPreference = "Stop"

if (-not (Test-Path "Assets\Scripts")) {
    Write-Error "未找到 Assets\Scripts 目录，请先 cd 到 Unity 项目根目录再运行此脚本。"
    exit 1
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Write-ScriptFile($relPath, $content) {
    $fullPath = Join-Path (Get-Location) $relPath
    $dir = Split-Path $fullPath -Parent
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    [System.IO.File]::WriteAllText($fullPath, $content.TrimStart("`r", "`n"), $utf8NoBom)
    Write-Host "[OK] $relPath" -ForegroundColor Green
}

# 1. Core/ElementDef.cs
Write-ScriptFile "Assets\Scripts\Core\ElementDef.cs" @'
using System.Runtime.InteropServices;

// 物态：用 byte 存储，保证结构体可以被 NativeArray / Burst 直接使用。
public enum ElementState : byte
{
    Vacuum = 0,  // 真空
    Solid = 1,   // 固体
    Liquid = 2,  // 液体
    Gas = 3      // 气体
}

// 材质的静态属性表。所有字段都是值类型，不含引用，适合放进 NativeArray。
// 内部统一使用开尔文 (K) 作为温度单位，质量使用 kg，热容/导热遵循《缺氧》DTU 口径。
[StructLayout(LayoutKind.Sequential)]
public struct ElementDef
{
    public ushort Id;          // 材质 ID，对应 ElementIds 常量，也是 ElementDefs 数组下标
    public byte State;         // 物态，取 ElementState 的 byte 值
    public byte IsInsulator;   // 1 = 隔热材质（走 min(kA, kB) * 1/255 衰减），0 = 普通材质

    // 比热容 DTU/(g·K)，数值上等价于 kJ/(kg·K)
    public float SHC;

    // 导热系数 DTU/(m·s·K)，数值上等价于 W/(m·K)；0 表示绝对绝热（仅用于真空）
    public float TC;

    // 密度 kg/m³：主要留给后续气体/液体 Pass 使用
    public float Density;

    // 摩尔质量 kg/mol：预留给气体扩散与质量交换 Pass
    public float MolarMass;

    // 单格最大质量 kg：同时用于质量概览的纹理归一化
    public float MaxMass;

    // 画刷和场景预设默认填入的质量 kg
    public float DefaultMass;

    // 低温相变阈值 (K)：凝固/液化点，预留给相变 Pass
    public float LowTemp;

    // 高温相变阈值 (K)：熔化/沸点，预留给相变 Pass
    public float HighTemp;

    // 相变潜热 kJ/kg：预留给相变 Pass
    public float LatentHeat;
}
'@

# 2. Core/ElementRegistry.cs
Write-ScriptFile "Assets\Scripts\Core\ElementRegistry.cs" @'
// 材质 ID 必须与 ElementRegistry.DisplayNames、BuildDefault 中的 defs 数组下标严格对应。
public static class ElementIds
{
    public const ushort Vacuum = 0;          // 真空：无质量、不导热
    public const ushort Insulation = 1;      // 隔热墙：带 IsInsulator 标记，极慢漏热而非绝对 0
    public const ushort Water = 2;           // 水：液体，高比热
    public const ushort Ice = 3;             // 冰：水的固态
    public const ushort Oxygen = 4;          // 氧气：气体
    public const ushort CarbonDioxide = 5;   // 二氧化碳：重气体
    public const ushort SuperConductor = 6;  // 超级导热体：极高 TC
}

public static class ElementRegistry
{
    public const float ZeroC = 273.15f;

    public static float CelsiusToKelvin(float celsius) => celsius + ZeroC;
    public static float KelvinToCelsius(float kelvin) => kelvin - ZeroC;

    // 画刷面板显示的名字，顺序必须与 ElementIds 一致。
    public static readonly string[] DisplayNames =
    {
        "Vacuum", "Insulation", "Water", "Ice", "Oxygen", "CO2", "SuperConductor"
    };

    private static ElementDef[] _defs;

    public static ElementDef[] BuildDefault()
    {
        var defs = new ElementDef[DisplayNames.Length];

        defs[ElementIds.Vacuum] = new ElementDef
        {
            Id = ElementIds.Vacuum,
            State = (byte)ElementState.Vacuum,
            IsInsulator = 0,
            SHC = 0f,
            TC = 0f,
            Density = 0f,
            MolarMass = 0f,
            MaxMass = 0f,
            DefaultMass = 0f,
            LowTemp = 0f,
            HighTemp = 0f,
            LatentHeat = 0f
        };

        // 隔热陶瓷口径：TC = 0.62，IsInsulator = 1（计算时按 min(kA, kB) / 255 衰减）
        defs[ElementIds.Insulation] = new ElementDef
        {
            Id = ElementIds.Insulation,
            State = (byte)ElementState.Solid,
            IsInsulator = 1,
            SHC = 0.84f,
            TC = 0.62f,
            Density = 1200f,
            MolarMass = 0f,
            MaxMass = 400f,
            DefaultMass = 200f,
            LowTemp = 1f,
            HighTemp = CelsiusToKelvin(1849.85f),
            LatentHeat = 0f
        };

        defs[ElementIds.Water] = new ElementDef
        {
            Id = ElementIds.Water,
            State = (byte)ElementState.Liquid,
            IsInsulator = 0,
            SHC = 4.179f,
            TC = 0.609f,
            Density = 1000f,
            MolarMass = 0.018f,
            MaxMass = 1000f,
            DefaultMass = 1000f,
            LowTemp = CelsiusToKelvin(-0.65f),
            HighTemp = CelsiusToKelvin(99.35f),
            LatentHeat = 334f
        };

        defs[ElementIds.Ice] = new ElementDef
        {
            Id = ElementIds.Ice,
            State = (byte)ElementState.Solid,
            IsInsulator = 0,
            SHC = 2.05f,
            TC = 2.18f,
            Density = 917f,
            MolarMass = 0.018f,
            MaxMass = 1000f,
            DefaultMass = 1000f,
            LowTemp = 1f,
            HighTemp = CelsiusToKelvin(-0.65f),
            LatentHeat = 334f
        };

        defs[ElementIds.Oxygen] = new ElementDef
        {
            Id = ElementIds.Oxygen,
            State = (byte)ElementState.Gas,
            IsInsulator = 0,
            SHC = 1.005f,
            TC = 0.024f,
            Density = 1.429f,
            MolarMass = 0.032f,
            MaxMass = 2f,
            DefaultMass = 1f,
            LowTemp = CelsiusToKelvin(-182.96f),
            HighTemp = 10000f,
            LatentHeat = 213f
        };

        defs[ElementIds.CarbonDioxide] = new ElementDef
        {
            Id = ElementIds.CarbonDioxide,
            State = (byte)ElementState.Gas,
            IsInsulator = 0,
            SHC = 0.846f,
            TC = 0.0146f,
            Density = 1.977f,
            MolarMass = 0.044f,
            MaxMass = 2f,
            DefaultMass = 1f,
            LowTemp = CelsiusToKelvin(-48.15f),
            HighTemp = 10000f,
            LatentHeat = 571f
        };

        // 超级导热体（类似导热质 / 钨钢实验材）
        defs[ElementIds.SuperConductor] = new ElementDef
        {
            Id = ElementIds.SuperConductor,
            State = (byte)ElementState.Solid,
            IsInsulator = 0,
            SHC = 0.386f,
            TC = 220f,
            Density = 8000f,
            MolarMass = 0f,
            MaxMass = 1000f,
            DefaultMass = 200f,
            LowTemp = 1f,
            HighTemp = CelsiusToKelvin(3000f),
            LatentHeat = 0f
        };

        _defs = (ElementDef[])defs.Clone();
        return defs;
    }

    public static ElementDef GetDef(ushort id)
    {
        if (_defs == null)
        {
            BuildDefault();
        }

        return id < _defs.Length ? _defs[id] : _defs[ElementIds.Vacuum];
    }

    public static string GetDisplayName(ushort id)
    {
        return id < DisplayNames.Length ? DisplayNames[id] : "Unknown";
    }
}
'@

# 3. Simulation/Thermal/ThermalConductionPass.cs
Write-ScriptFile "Assets\Scripts\Simulation\Thermal\ThermalConductionPass.cs" @'
using Unity.Jobs;

public sealed class ThermalConductionPass : ISimPass
{
    public JobHandle Schedule(SimGridData grid, float dt, int tickIndex, JobHandle dependency)
    {
        JobHandle handle = dependency;

        // 偶数 Tick 正序、奇数 Tick 逆序，抵消 4-Pass 串行带来的微小方向偏置
        if ((tickIndex & 1) == 0)
        {
            handle = ScheduleAxisPass(grid, dt, 0, 0, handle); // 横偶
            handle = ScheduleAxisPass(grid, dt, 0, 1, handle); // 横奇
            handle = ScheduleAxisPass(grid, dt, 1, 0, handle); // 纵偶
            handle = ScheduleAxisPass(grid, dt, 1, 1, handle); // 纵奇
        }
        else
        {
            handle = ScheduleAxisPass(grid, dt, 1, 1, handle); // 纵奇
            handle = ScheduleAxisPass(grid, dt, 1, 0, handle); // 纵偶
            handle = ScheduleAxisPass(grid, dt, 0, 1, handle); // 横奇
            handle = ScheduleAxisPass(grid, dt, 0, 0, handle); // 横偶
        }

        return handle;
    }

    private static JobHandle ScheduleAxisPass(SimGridData grid, float dt, int axis, int parity, JobHandle dep)
    {
        int divisor = axis == 0 ? grid.Width - parity : grid.Height - parity;
        if (divisor <= 1)
        {
            return dep;
        }

        int pairsPerLine = divisor / 2;
        int pairCount = axis == 0 ? pairsPerLine * grid.Height : pairsPerLine * grid.Width;

        var job = new ThermalPairPassJob
        {
            Width = grid.Width,
            Height = grid.Height,
            Dt = dt,
            Axis = axis,
            Parity = parity,
            ElementDefs = grid.ElementDefs,
            ElementIds = grid.ElementIds,
            Masses = grid.Masses,
            Temperatures = grid.Temperatures
        };

        return job.Schedule(pairCount, 64, dep);
    }
}
'@

# 4. Simulation/Thermal/ThermalPairPassJob.cs
Write-ScriptFile "Assets\Scripts\Simulation\Thermal\ThermalPairPassJob.cs" @'
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
public struct ThermalPairPassJob : IJobParallelFor
{
    private const float InsulatorFactor = 1f / 255f;
    private const float MinMassKg = 1e-4f;
    private const float TempDeadzoneK = 0.01f;
    private const float MinTempK = 1f;
    private const float MaxTempK = 10000f;

    [ReadOnly] public NativeArray<ElementDef> ElementDefs;
    [ReadOnly] public NativeArray<ushort> ElementIds;
    [ReadOnly] public NativeArray<float> Masses;

    // 奇偶配对保证同一 Pass 内每个格子只被一个线程读写，完全无锁
    [NativeDisableParallelForRestriction]
    public NativeArray<float> Temperatures;

    public int Width;
    public int Height;
    public float Dt;
    public int Axis;
    public int Parity;

    public void Execute(int i)
    {
        if (Dt <= 0f || ElementDefs.Length == 0)
        {
            return;
        }

        int lineDivisor = Axis == 0 ? Width - Parity : Height - Parity;
        if (lineDivisor <= 1)
        {
            return;
        }

        int pairsPerLine = lineDivisor / 2;
        int aIndex;
        int bIndex;

        if (Axis == 0)
        {
            int pair = i % pairsPerLine;
            int y = i / pairsPerLine;
            int x = pair * 2 + Parity;
            aIndex = y * Width + x;
            bIndex = aIndex + 1;
        }
        else
        {
            int pair = i % pairsPerLine;
            int x = i / pairsPerLine;
            int y = pair * 2 + Parity;
            aIndex = y * Width + x;
            bIndex = aIndex + Width;
        }

        float mA = Masses[aIndex];
        float mB = Masses[bIndex];
        if (mA <= MinMassKg || mB <= MinMassKg)
        {
            return;
        }

        ushort idA = ElementIds[aIndex];
        ushort idB = ElementIds[bIndex];
        if (idA >= ElementDefs.Length || idB >= ElementDefs.Length)
        {
            return;
        }

        ElementDef defA = ElementDefs[idA];
        ElementDef defB = ElementDefs[idB];
        if (defA.State == (byte)ElementState.Vacuum || defB.State == (byte)ElementState.Vacuum)
        {
            return;
        }

        float tA = Temperatures[aIndex];
        float tB = Temperatures[bIndex];
        float deltaT = tA - tB;

        if (math.abs(deltaT) < TempDeadzoneK)
        {
            return;
        }

        // 1. 等效热导率 k_eff（对齐《缺氧》：隔热走 min/255，普通走几何平均 * 状态倍率）
        float kEff;
        if (defA.IsInsulator == 1 || defB.IsInsulator == 1)
        {
            kEff = math.min(defA.TC, defB.TC) * InsulatorFactor;
        }
        else
        {
            kEff = math.sqrt(defA.TC * defB.TC) * GetStateMultiplier(defA.State, defB.State);
        }

        if (kEff <= 0f)
        {
            return;
        }

        // 2. 统一 DTU 量纲：Mass(kg) * 1000(g/kg) * SHC(DTU/g·K) = 热容(DTU/K)
        float capA = mA * 1000f * defA.SHC;
        float capB = mB * 1000f * defB.SHC;
        if (capA <= 1e-5f || capB <= 1e-5f)
        {
            return;
        }

        // 原始传热量 q (DTU)
        float q = deltaT * kEff * Dt * 1000f;

        // 3. 防震荡硬截断：单步温变不超过总温差的 1/4 且绝不越过热平衡温度 T_eq
        float tEq = (capA * tA + capB * tB) / (capA + capB);
        float maxDeltaTA = math.min(math.abs(tA - tEq), math.abs(deltaT) * 0.25f);
        float maxDeltaTB = math.min(math.abs(tB - tEq), math.abs(deltaT) * 0.25f);
        float maxSafeQ = math.min(maxDeltaTA * capA, maxDeltaTB * capB);

        q = math.clamp(q, -maxSafeQ, maxSafeQ);

        // 4. 严格能量守恒写回（开尔文）
        Temperatures[aIndex] = math.clamp(tA - q / capA, MinTempK, MaxTempK);
        Temperatures[bIndex] = math.clamp(tB + q / capB, MinTempK, MaxTempK);
    }

    private static float GetStateMultiplier(byte a, byte b)
    {
        if (a == (byte)ElementState.Solid && b == (byte)ElementState.Solid) return 1.0f;
        if (a == (byte)ElementState.Liquid && b == (byte)ElementState.Liquid) return 625.0f;
        if (a == (byte)ElementState.Gas && b == (byte)ElementState.Gas) return 25.0f;
        if ((a == (byte)ElementState.Solid && b == (byte)ElementState.Gas) ||
            (a == (byte)ElementState.Gas && b == (byte)ElementState.Solid)) return 25.0f;
        return 1.0f; // 固-液、气-液（对齐《缺氧》原版）
    }
}
'@

# 5. Runtime/SimBootstrap.cs
Write-ScriptFile "Assets\Scripts\Runtime\SimBootstrap.cs" @'
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
'@

# 6. Runtime/SimSceneInitializer.cs
Write-ScriptFile "Assets\Scripts\Runtime\SimSceneInitializer.cs" @'
using UnityEngine;

public static class SimSceneInitializer
{
    public static void Apply(SimGridData grid)
    {
        // 真空统一初始化为 0K，便于热力图 Shader 零开销识别
        for (int i = 0; i < grid.CellCount; i++)
        {
            grid.ElementIds[i] = ElementIds.Vacuum;
            grid.Masses[i] = 0f;
            grid.Temperatures[i] = 0f;
        }

        // 四周隔热边界（20°C）
        for (int x = 0; x < grid.Width; x++)
        {
            SetCellCelsius(grid, x, 0, ElementIds.Insulation, 20f);
            SetCellCelsius(grid, x, grid.Height - 1, ElementIds.Insulation, 20f);
        }

        for (int y = 0; y < grid.Height; y++)
        {
            SetCellCelsius(grid, 0, y, ElementIds.Insulation, 20f);
            SetCellCelsius(grid, grid.Width - 1, y, ElementIds.Insulation, 20f);
        }

        // 底部冷水池（12°C）
        int poolTop = Mathf.Clamp(grid.Height / 4, 1, grid.Height - 2);
        for (int y = 1; y <= poolTop; y++)
        {
            for (int x = 1; x < grid.Width - 1; x++)
            {
                SetCellCelsius(grid, x, y, ElementIds.Water, 12f);
            }
        }

        // 上方常温氧气室（22°C）
        int airBottom = Mathf.Clamp(grid.Height / 2, poolTop + 1, grid.Height - 2);
        int airLeft = grid.Width / 4;
        int airRight = grid.Width - airLeft - 1;

        for (int y = airBottom; y < grid.Height - 1; y++)
        {
            for (int x = airLeft; x <= airRight; x++)
            {
                SetCellCelsius(grid, x, y, ElementIds.Oxygen, 22f);
            }
        }

        // 氧气室内的局部高温点（90°C）
        int hotY = Mathf.Min(airBottom + 2, grid.Height - 2);
        int hotX = grid.Width / 2;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (grid.InBounds(hotX + dx, hotY + dy))
                {
                    SetCellCelsius(grid, hotX + dx, hotY + dy, ElementIds.Oxygen, 90f);
                }
            }
        }
    }

    private static void SetCellCelsius(SimGridData grid, int x, int y, ushort elementId, float tempCelsius)
    {
        if (!grid.InBounds(x, y))
        {
            return;
        }

        ElementDef def = ElementRegistry.GetDef(elementId);
        int index = grid.ToIndex(x, y);
        grid.ElementIds[index] = elementId;
        grid.Masses[index] = def.DefaultMass;
        grid.Temperatures[index] = elementId == ElementIds.Vacuum
            ? 0f
            : ElementRegistry.CelsiusToKelvin(tempCelsius);
    }
}
'@

# 7. Presentation/SimVisualizer.cs
Write-ScriptFile "Assets\Scripts\Presentation\SimVisualizer.cs" @'
using Unity.Collections;
using UnityEngine;

public enum SimOverlayMode
{
    Temperature = 0,
    Material = 1,
    Mass = 2
}

public sealed class SimVisualizer : MonoBehaviour
{
    [SerializeField] private Shader heatmapShader;
    [SerializeField] private float minTempC = -20f;
    [SerializeField] private float maxTempC = 100f;
    [SerializeField] private SimOverlayMode overlayMode = SimOverlayMode.Temperature;

    private SimGridData _grid;
    private Texture2D _texture;
    private Material _material;
    private Mesh _mesh;
    private MeshRenderer _renderer;

    private static readonly int ScalarTexId = Shader.PropertyToID("_ScalarTex");
    private static readonly int ModeId = Shader.PropertyToID("_Mode");
    private static readonly int MinTempKId = Shader.PropertyToID("_MinTempK");
    private static readonly int MaxTempKId = Shader.PropertyToID("_MaxTempK");

    public SimOverlayMode Mode
    {
        get => overlayMode;
        set => overlayMode = value;
    }

    public float MinTempC
    {
        get => minTempC;
        set => minTempC = value;
    }

    public float MaxTempC
    {
        get => maxTempC;
        set => maxTempC = value;
    }

    public void Initialize(SimGridData grid)
    {
        _grid = grid;

        _texture = new Texture2D(grid.Width, grid.Height, TextureFormat.RFloat, false, true)
        {
            name = "SimScalarField",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        Shader shader = heatmapShader != null ? heatmapShader : Shader.Find("XNotInclude/ThermalHeatmap");
        if (shader == null)
        {
            Debug.LogError("ThermalHeatmap shader not found. Check Presentation/Shaders/ThermalHeatmap.shader.");
            return;
        }

        _material = new Material(shader) { name = "SimHeatmapMaterial" };
        _material.SetTexture(ScalarTexId, _texture);

        _mesh = BuildQuad(grid.Width, grid.Height);

        var view = new GameObject("SimGridView");
        view.transform.SetParent(transform, false);
        view.AddComponent<MeshFilter>().sharedMesh = _mesh;

        _renderer = view.AddComponent<MeshRenderer>();
        _renderer.sharedMaterial = _material;
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows = false;

        Refresh();
    }

    public void Refresh()
    {
        if (_grid == null || _texture == null)
        {
            return;
        }

        NativeArray<float> raw = _texture.GetRawTextureData<float>();

        switch (overlayMode)
        {
            case SimOverlayMode.Temperature:
                // 零 GC 直接整块拷贝到纹理显存缓冲，归一化交给 GPU Shader
                raw.CopyFrom(_grid.Temperatures);
                break;

            case SimOverlayMode.Material:
                for (int i = 0; i < raw.Length; i++)
                {
                    raw[i] = _grid.ElementIds[i];
                }
                break;

            case SimOverlayMode.Mass:
                for (int i = 0; i < raw.Length; i++)
                {
                    ushort id = _grid.ElementIds[i];
                    ElementDef def = id < _grid.ElementDefs.Length ? _grid.ElementDefs[id] : default;
                    float maxMass = def.MaxMass > 0f ? def.MaxMass : 1f;
                    raw[i] = Mathf.Clamp01(_grid.Masses[i] / maxMass);
                }
                break;
        }

        _texture.Apply(false, false);

        if (_material != null)
        {
            _material.SetFloat(ModeId, (float)overlayMode);
            _material.SetFloat(MinTempKId, ElementRegistry.CelsiusToKelvin(minTempC));
            _material.SetFloat(MaxTempKId, ElementRegistry.CelsiusToKelvin(maxTempC));
        }
    }

    public void Dispose()
    {
        if (_renderer != null) Destroy(_renderer.gameObject);
        if (_mesh != null) Destroy(_mesh);
        if (_material != null) Destroy(_material);
        if (_texture != null) Destroy(_texture);

        _grid = null;
    }

    private static Mesh BuildQuad(int width, int height)
    {
        float halfWidth = width * 0.5f;
        float halfHeight = height * 0.5f;

        var mesh = new Mesh { name = "SimGridQuad" };
        mesh.vertices = new[]
        {
            new Vector3(-halfWidth, -halfHeight, 0f),
            new Vector3(halfWidth, -halfHeight, 0f),
            new Vector3(halfWidth, halfHeight, 0f),
            new Vector3(-halfWidth, halfHeight, 0f)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateBounds();
        return mesh;
    }
}
'@

# 8. Presentation/Shaders/ThermalHeatmap.shader
Write-ScriptFile "Assets\Scripts\Presentation\Shaders\ThermalHeatmap.shader" @'
Shader "XNotInclude/ThermalHeatmap"
{
    Properties
    {
        _ScalarTex ("Scalar Field (RFloat)", 2D) = "black" {}
        _Mode ("Mode (0=Temp, 1=Material, 2=Mass)", Float) = 0
        _MinTempK ("Min Temp (K)", Float) = 253.15
        _MaxTempK ("Max Temp (K)", Float) = 373.15
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_ScalarTex);
            SAMPLER(sampler_ScalarTex);

            float _Mode;
            float _MinTempK;
            float _MaxTempK;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half3 HeatRamp(float t)
            {
                t = saturate(t);
                half3 cold = half3(0.03, 0.08, 0.45);
                half3 cool = half3(0.00, 0.62, 0.88);
                half3 mid  = half3(0.14, 0.84, 0.34);
                half3 warm = half3(0.98, 0.84, 0.12);
                half3 hot  = half3(0.94, 0.12, 0.06);

                if (t < 0.25) return lerp(cold, cool, t * 4.0);
                if (t < 0.50) return lerp(cool, mid,  (t - 0.25) * 4.0);
                if (t < 0.75) return lerp(mid,  warm, (t - 0.50) * 4.0);
                return lerp(warm, hot, (t - 0.75) * 4.0);
            }

            half3 ElementRamp(float id)
            {
                float phase = id * 2.399963;
                return half3(
                    0.5 + 0.5 * sin(phase),
                    0.5 + 0.5 * sin(phase + 2.094395),
                    0.5 + 0.5 * sin(phase + 4.188790)
                );
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float scalar = SAMPLE_TEXTURE2D(_ScalarTex, sampler_ScalarTex, IN.uv).r;
                half3 vacuumColor = half3(0.05, 0.05, 0.08);

                // Mode 1: 材质概览
                if (_Mode > 0.5 && _Mode < 1.5)
                {
                    if (scalar < 0.5) return half4(vacuumColor, 1.0);
                    return half4(ElementRamp(floor(scalar + 0.5)), 1.0);
                }

                // Mode 2: 质量概览
                if (_Mode >= 1.5)
                {
                    if (scalar <= 0.0001) return half4(vacuumColor, 1.0);
                    return half4(HeatRamp(scalar), 1.0);
                }

                // Mode 0: 温度热力图（真空 = 0K，非真空 >= 1K）
                if (scalar < 0.5) return half4(vacuumColor, 1.0);
                float norm = (scalar - _MinTempK) / max(0.001, _MaxTempK - _MinTempK);
                return half4(HeatRamp(norm), 1.0);
            }
            ENDHLSL
        }
    }
}
'@

# 9. Runtime/SimDebugOverlay.cs
Write-ScriptFile "Assets\Scripts\Runtime\SimDebugOverlay.cs" @'
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
'@

Write-Host "`n全部 9 个文件已更新完成，切回 Unity 等待重新编译即可。" -ForegroundColor Cyan