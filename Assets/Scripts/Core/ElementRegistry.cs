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