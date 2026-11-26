using UnityEngine;

public static class SimSceneInitializer
{
    public static void Apply(SimGridData grid)
    {
        for (int i = 0; i < grid.CellCount; i++)
        {
            grid.ElementIds[i] = ElementIds.Vacuum;
            grid.Masses[i] = 0f;
            grid.Temperatures[i] = 20f;
        }

        for (int x = 0; x < grid.Width; x++)
        {
            SetCell(grid, x, 0, ElementIds.Insulation, 20f);
            SetCell(grid, x, grid.Height - 1, ElementIds.Insulation, 20f);
        }

        for (int y = 0; y < grid.Height; y++)
        {
            SetCell(grid, 0, y, ElementIds.Insulation, 20f);
            SetCell(grid, grid.Width - 1, y, ElementIds.Insulation, 20f);
        }

        int poolTop = Mathf.Clamp(grid.Height / 4, 1, grid.Height - 2);
        for (int y = 1; y <= poolTop; y++)
        {
            for (int x = 1; x < grid.Width - 1; x++)
            {
                SetCell(grid, x, y, ElementIds.Water, 12f);
            }
        }

        int airBottom = Mathf.Clamp(grid.Height / 2, poolTop + 1, grid.Height - 2);
        int airLeft = grid.Width / 4;
        int airRight = grid.Width - airLeft - 1;

        for (int y = airBottom; y < grid.Height - 1; y++)
        {
            for (int x = airLeft; x <= airRight; x++)
            {
                SetCell(grid, x, y, ElementIds.Oxygen, 22f);
            }
        }

        int hotY = Mathf.Min(airBottom + 2, grid.Height - 2);
        int hotX = grid.Width / 2;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (grid.InBounds(hotX + dx, hotY + dy))
                {
                    SetCell(grid, hotX + dx, hotY + dy, ElementIds.Oxygen, 90f);
                }
            }
        }
    }

    private static void SetCell(SimGridData grid, int x, int y, ushort elementId, float temperature)
    {
        if (!grid.InBounds(x, y))
        {
            return;
        }

        ElementDef def = ElementRegistry.GetDef(elementId);
        int index = grid.ToIndex(x, y);
        grid.ElementIds[index] = elementId;
        grid.Masses[index] = def.DefaultMass;
        grid.Temperatures[index] = temperature;
    }
}
