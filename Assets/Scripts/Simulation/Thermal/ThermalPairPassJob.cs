using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

[BurstCompile]
public struct ThermalPairPassJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<ElementDef> ElementDefs;
    [ReadOnly] public NativeArray<ushort> ElementIds;
    [ReadOnly] public NativeArray<float> Masses;

    // Each job index owns a disjoint pair; parity guarantees no two indices
    // write the same cell in the same pass.
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

        int cellCount = Width * Height;
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

        if (aIndex < 0 || bIndex < 0 || aIndex >= cellCount || bIndex >= cellCount)
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

        float heatCapacityA = Masses[aIndex] * defA.SHC;
        float heatCapacityB = Masses[bIndex] * defB.SHC;

        if (heatCapacityA <= 1e-5f || heatCapacityB <= 1e-5f)
        {
            return;
        }

        float conductance = defA.TC < defB.TC ? defA.TC : defB.TC;
        if (conductance <= 0f)
        {
            return;
        }

        // Cap the transfer at 45% of the smaller heat capacity so a single
        // pass can never overshoot equilibrium, regardless of dt or TC.
        float smallerHeatCapacity = heatCapacityA < heatCapacityB ? heatCapacityA : heatCapacityB;
        float maxConductance = 0.45f * smallerHeatCapacity / Dt;
        float effectiveConductance = conductance < maxConductance ? conductance : maxConductance;

        float deltaHeat = effectiveConductance * (Temperatures[bIndex] - Temperatures[aIndex]) * Dt;

        Temperatures[aIndex] += deltaHeat / heatCapacityA;
        Temperatures[bIndex] -= deltaHeat / heatCapacityB;
    }
}
