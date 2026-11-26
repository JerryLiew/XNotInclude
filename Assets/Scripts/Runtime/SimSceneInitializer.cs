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

        // 顶部偏左放置一小团二氧化碳（35°C），用于直观验证重力下沉分层与横向摊平
        int co2Top = grid.Height - 3;
        int co2Bottom = Mathf.Max(airBottom + 1, co2Top - 3);
        int co2Left = Mathf.Max(airLeft + 1, grid.Width / 2 - 5);
        int co2Right = Mathf.Min(airRight - 1, co2Left + 8);
        for (int y = co2Bottom; y <= co2Top; y++)
        {
            for (int x = co2Left; x <= co2Right; x++)
            {
                SetCellCelsius(grid, x, y, ElementIds.CarbonDioxide, 35f);
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