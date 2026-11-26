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