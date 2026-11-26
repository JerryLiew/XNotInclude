// 材质 ID 必须与 ElementRegistry.DisplayNames、BuildDefault 中的 defs 数组下标严格对应。
public static class ElementIds
{
    public const ushort Vacuum = 0;          // 真空：无质量、不导热，表示空格子
    public const ushort Insulation = 1;      // 隔热墙：TC = 0，完全阻断热传导
    public const ushort Water = 2;           // 水：液体，高比热，默认 1 kg/格
    public const ushort Ice = 3;             // 冰：水的固态，导热比水快
    public const ushort Oxygen = 4;          // 氧气：气体，低密度、低导热，预留气体扩散用
    public const ushort CarbonDioxide = 5;   // 二氧化碳：比氧气更重的气体，预留气体分层用
    public const ushort SuperConductor = 6;  // 超级导热体：TC 极大，几乎瞬间拉平温差
}

public static class ElementRegistry
{
    // 画刷面板显示的名字，顺序必须与上面的 ElementIds 一致，否则选到的材质会错位。
    public static readonly string[] DisplayNames =
    {
        "Vacuum", "Insulation", "Water", "Ice", "Oxygen", "CO2", "SuperConductor"
    };

    private static ElementDef[] _defs;

    // 所有材质的静态属性都在这里定义；各字段的单位见 ElementDef.cs。
    public static ElementDef[] BuildDefault()
    {
        var defs = new ElementDef[DisplayNames.Length];

        // 真空：0 质量、0 热容、0 导热，成对导热 Job 会直接跳过这个格子。
        defs[ElementIds.Vacuum] = new ElementDef
        {
            Id = ElementIds.Vacuum,
            State = (byte)ElementState.Vacuum,
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

        // 隔热墙：TC = 0，热量穿不过去，常用来做房间的边界。
        defs[ElementIds.Insulation] = new ElementDef
        {
            Id = ElementIds.Insulation,
            State = (byte)ElementState.Solid,
            SHC = 0.2f,
            TC = 0f,
            Density = 1200f,
            MolarMass = 0f,
            MaxMass = 20f,
            DefaultMass = 10f,
            LowTemp = -273f,
            HighTemp = 1000f,
            LatentHeat = 0f
        };

        // 水：液体，SHC 4.18 kJ/(kg·K)，默认 1 kg/格。
        // LowTemp / HighTemp 是 0~100°C，预留给后续相变 Pass 使用。
        defs[ElementIds.Water] = new ElementDef
        {
            Id = ElementIds.Water,
            State = (byte)ElementState.Liquid,
            SHC = 4.18f,
            TC = 0.6f,
            Density = 1000f,
            MolarMass = 0.018f,
            MaxMass = 10f,
            DefaultMass = 1f,
            LowTemp = 0f,
            HighTemp = 100f,
            LatentHeat = 334f
        };

        // 冰：水的固态，TC 2.2 比水导热快；HighTemp = 0°C 即熔点。
        defs[ElementIds.Ice] = new ElementDef
        {
            Id = ElementIds.Ice,
            State = (byte)ElementState.Solid,
            SHC = 2.1f,
            TC = 2.2f,
            Density = 917f,
            MolarMass = 0.018f,
            MaxMass = 10f,
            DefaultMass = 1f,
            LowTemp = -273f,
            HighTemp = 0f,
            LatentHeat = 334f
        };

        // 氧气：气体，SHC 0.918、TC 0.026，低密度低导热。
        // MolarMass 0.032 kg/mol 预留给后续气体摩尔质量交换 Pass。
        defs[ElementIds.Oxygen] = new ElementDef
        {
            Id = ElementIds.Oxygen,
            State = (byte)ElementState.Gas,
            SHC = 0.918f,
            TC = 0.026f,
            Density = 1.429f,
            MolarMass = 0.032f,
            MaxMass = 2f,
            DefaultMass = 0.2f,
            LowTemp = -218.8f,
            HighTemp = -182.9f,
            LatentHeat = 213f
        };

        // 二氧化碳：气体，密度和摩尔质量都比氧气大，预留给气体分层/扩散实验。
        defs[ElementIds.CarbonDioxide] = new ElementDef
        {
            Id = ElementIds.CarbonDioxide,
            State = (byte)ElementState.Gas,
            SHC = 0.844f,
            TC = 0.017f,
            Density = 1.977f,
            MolarMass = 0.044f,
            MaxMass = 2f,
            DefaultMass = 0.2f,
            LowTemp = -100f,
            HighTemp = -78.5f,
            LatentHeat = 571f
        };

        // 超级导热体：TC 50000 W/(m·K)、SHC 0.05，几步内就能把温差拉平。
        // 稳定性限幅会防止它让温度出现过冲，适合观察极端导热行为。
        defs[ElementIds.SuperConductor] = new ElementDef
        {
            Id = ElementIds.SuperConductor,
            State = (byte)ElementState.Solid,
            SHC = 0.05f,
            TC = 50000f,
            Density = 8000f,
            MolarMass = 0f,
            MaxMass = 10f,
            DefaultMass = 5f,
            LowTemp = -273f,
            HighTemp = 3000f,
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
