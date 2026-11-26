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
[StructLayout(LayoutKind.Sequential)]
public struct ElementDef
{
    public ushort Id;   // 材质 ID，对应 ElementIds 常量，也是 ElementDefs 数组下标
    public byte State;  // 物态，取 ElementState 的 byte 值

    // 比热容 kJ/(kg·K)：每 kg 升高 1°C 需要的热量，越大升温越慢
    public float SHC;

    // 导热系数 W/(m·K)：越大传热越快；0 表示绝热
    public float TC;

    // 密度 kg/m³：主要留给后续气体/液体 Pass 使用
    public float Density;

    // 摩尔质量 kg/mol：预留给气体扩散与质量交换 Pass
    public float MolarMass;

    // 单格最大质量 kg：同时用于质量概览的纹理归一化
    public float MaxMass;

    // 画刷和场景预设默认填入的质量 kg
    public float DefaultMass;

    // 低温相变阈值 °C：凝固/液化点，预留给相变 Pass
    public float LowTemp;

    // 高温相变阈值 °C：熔化/沸点，预留给相变 Pass
    public float HighTemp;

    // 相变潜热 kJ/kg：预留给相变 Pass 计算吸收/释放的热量
    public float LatentHeat;
}
