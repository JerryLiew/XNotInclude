using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
public struct GasPairPassJob : IJobParallelFor
{
    private const float FlowRatePerSec = 1.6f;          // 同种气体均压速率系数（dt=0.2s 时单步转移 32% 压差）
    private const float VacuumSpreadRatePerSec = 1.5f;  // 向真空扩张速率系数（dt=0.2s 时单步扩出 30%）
    private const float MaxFlowFraction = 0.45f;        // 单步最高转移比例，防止越过均值震荡

    private const float MinSpreadMassKg = 0.02f;        // 允许向真空扩张的最小源质量（20g）
    private const float MinSpreadTransferKg = 0.005f;   // 单次向真空扩张的最小转移量（5g），与吸干阈值拉开滞回带
    private const float DrainThresholdKg = 0.002f;      // 低于 2g 的残留微量气体碰到同种大质量格直接吸干归零
    private const float MassDiffDeadzoneKg = 1e-4f;     // 质量差死区（0.1g）
    private const float ZeroMassEpsilon = 1e-7f;        // 清理异常 0 质量气体格

    private const float GravitySwapChance = 0.85f;      // 重气体在轻气体上方时的下沉互换概率
    private const float SameMolarSwapChance = 0.05f;    // 等摩尔质量异种气体的纵向微弱混合概率
    private const float HorizontalSwapChance = 0.12f;   // 横向异种气体小概率互换（帮助沉底气体摊平）

    [ReadOnly] public NativeArray<ElementDef> ElementDefs;

    // 奇偶配对保证同一 Pass 内每对格子互不重叠，可安全无锁并行原地读写
    [NativeDisableParallelForRestriction] public NativeArray<ushort> ElementIds;
    [NativeDisableParallelForRestriction] public NativeArray<float> Masses;
    [NativeDisableParallelForRestriction] public NativeArray<float> Temperatures;

    public int Width;
    public int Height;
    public float Dt;
    public int Axis;
    public int Parity;
    public int TickIndex;

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

        ushort idA = ElementIds[aIndex];
        ushort idB = ElementIds[bIndex];
        if (idA >= ElementDefs.Length || idB >= ElementDefs.Length)
        {
            return;
        }

        ElementDef defA = ElementDefs[idA];
        ElementDef defB = ElementDefs[idB];

        bool aIsGas = defA.State == (byte)ElementState.Gas;
        bool bIsGas = defB.State == (byte)ElementState.Gas;
        bool aIsVac = defA.State == (byte)ElementState.Vacuum;
        bool bIsVac = defB.State == (byte)ElementState.Vacuum;

        // 清理异常的 0 质量气体格，防止成为幽灵障碍
        if (aIsGas && Masses[aIndex] <= ZeroMassEpsilon)
        {
            SetVacuum(aIndex);
            aIsGas = false;
            aIsVac = true;
            idA = global::ElementIds.Vacuum;
        }
        if (bIsGas && Masses[bIndex] <= ZeroMassEpsilon)
        {
            SetVacuum(bIndex);
            bIsGas = false;
            bIsVac = true;
            idB = global::ElementIds.Vacuum;
        }

        // 只处理 气体-气体 或 气体-真空 接触面
        if (!(aIsGas || bIsGas))
        {
            return;
        }
        if (!(aIsGas || aIsVac) || !(bIsGas || bIsVac))
        {
            return;
        }

        // 情况 1：气体向真空扩张
        if (aIsGas && bIsVac)
        {
            ExpandToVacuum(aIndex, bIndex, idA);
            return;
        }
        if (bIsGas && aIsVac)
        {
            ExpandToVacuum(bIndex, aIndex, idB);
            return;
        }

        // 情况 2：同种气体均压扩散 + 热量平流 + 微量吸干
        if (idA == idB)
        {
            EqualizeSameGas(aIndex, bIndex);
            return;
        }

        // 情况 3：异种气体重力分层 / 横向扩散
        ExchangeDifferentGas(aIndex, bIndex, idA, idB, defA, defB, i);
    }

    private void ExpandToVacuum(int src, int dst, ushort srcId)
    {
        float mSrc = Masses[src];
        if (mSrc < MinSpreadMassKg)
        {
            return;
        }

        float fraction = math.clamp(VacuumSpreadRatePerSec * Dt, 0.05f, MaxFlowFraction);
        float dm = mSrc * fraction;
        if (dm < MinSpreadTransferKg)
        {
            return;
        }

        float tSrc = Temperatures[src];
        ElementIds[dst] = srcId;
        Masses[dst] = dm;
        Temperatures[dst] = tSrc;
        Masses[src] = mSrc - dm;
    }

    private void EqualizeSameGas(int idxA, int idxB)
    {
        float mA = Masses[idxA];
        float mB = Masses[idxB];

        int high = mA >= mB ? idxA : idxB;
        int low = mA >= mB ? idxB : idxA;
        float mHigh = mA >= mB ? mA : mB;
        float mLow = mA >= mB ? mB : mA;

        // 微量吸干：极低残留（<2g）碰到更大的同种气团时整格合并，消除幽灵堵路
        if (mLow <= DrainThresholdKg && mHigh > mLow)
        {
            float tHighDrain = Temperatures[high];
            float tLowDrain = Temperatures[low];
            float totalMass = mHigh + mLow;
            Temperatures[high] = (mHigh * tHighDrain + mLow * tLowDrain) / totalMass;
            Masses[high] = totalMass;
            SetVacuum(low);
            return;
        }

        float diff = mHigh - mLow;
        if (diff < MassDiffDeadzoneKg)
        {
            return;
        }

        float fraction = math.clamp(FlowRatePerSec * Dt, 0.02f, MaxFlowFraction);
        float dm = diff * fraction;
        if (dm <= ZeroMassEpsilon)
        {
            return;
        }

        // 同种气体比热容一致，按质量加权混合温度即可实现 100% 严格能量守恒
        float tHigh = Temperatures[high];
        float tLow = Temperatures[low];
        float newMassLow = mLow + dm;

        Temperatures[low] = (mLow * tLow + dm * tHigh) / newMassLow;
        Masses[low] = newMassLow;
        Masses[high] = mHigh - dm;
    }

    private void ExchangeDifferentGas(
        int idxA, int idxB,
        ushort idA, ushort idB,
        ElementDef defA, ElementDef defB,
        int pairIndex)
    {
        float rand01 = NextRand01(pairIndex);

        if (Axis == 1)
        {
            // 纵向：idxA 在下 (y)，idxB 在上 (y+1)
            float molarDiff = defB.MolarMass - defA.MolarMass;
            if (molarDiff > 1e-4f)
            {
                // 上方气体更重（如 CO2 在 O2 上方），高概率下沉互换
                if (rand01 < GravitySwapChance)
                {
                    SwapCells(idxA, idxB, idA, idB);
                }
            }
            else if (math.abs(molarDiff) <= 1e-4f)
            {
                if (rand01 < SameMolarSwapChance)
                {
                    SwapCells(idxA, idxB, idA, idB);
                }
            }
        }
        else
        {
            // 横向：小概率互换让沉底或受阻的重气体自然摊平
            if (rand01 < HorizontalSwapChance)
            {
                SwapCells(idxA, idxB, idA, idB);
            }
        }
    }

    private void SwapCells(int idxA, int idxB, ushort idA, ushort idB)
    {
        float mA = Masses[idxA];
        float tA = Temperatures[idxA];

        ElementIds[idxA] = idB;
        Masses[idxA] = Masses[idxB];
        Temperatures[idxA] = Temperatures[idxB];

        ElementIds[idxB] = idA;
        Masses[idxB] = mA;
        Temperatures[idxB] = tA;
    }

    private void SetVacuum(int idx)
    {
        ElementIds[idx] = global::ElementIds.Vacuum;
        Masses[idx] = 0f;
        Temperatures[idx] = 0f;
    }

    private float NextRand01(int pairIndex)
    {
        uint h = math.hash(new uint4(
            (uint)TickIndex * 747796405u + 2891336453u,
            (uint)Axis * 277803737u + (uint)Parity,
            (uint)pairIndex,
            0x9E3779B9u));
        return (h & 0x00FFFFFFu) * (1f / 16777216f);
    }
}