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