using System;
using Unity.Collections;

public sealed class SimGridData : IDisposable
{
    public readonly int Width;
    public readonly int Height;

    public int CellCount => Width * Height;

    public NativeArray<ElementDef> ElementDefs;
    public NativeArray<ushort> ElementIds;
    public NativeArray<float> Masses;
    public NativeArray<float> Temperatures;

    public SimGridData(int width, int height, ElementDef[] defs)
    {
        if (width < 1 || height < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Grid dimensions must be positive.");
        }

        if (defs == null || defs.Length == 0)
        {
            throw new ArgumentException("At least one element definition is required.", nameof(defs));
        }

        Width = width;
        Height = height;

        ElementDefs = new NativeArray<ElementDef>(defs, Allocator.Persistent);
        ElementIds = new NativeArray<ushort>(CellCount, Allocator.Persistent);
        Masses = new NativeArray<float>(CellCount, Allocator.Persistent);
        Temperatures = new NativeArray<float>(CellCount, Allocator.Persistent);
    }

    public int ToIndex(int x, int y) => y * Width + x;

    public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

    public void Dispose()
    {
        if (ElementDefs.IsCreated) ElementDefs.Dispose();
        if (ElementIds.IsCreated) ElementIds.Dispose();
        if (Masses.IsCreated) Masses.Dispose();
        if (Temperatures.IsCreated) Temperatures.Dispose();
    }
}
