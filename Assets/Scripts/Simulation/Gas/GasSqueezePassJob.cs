using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

/// <summary>
/// 跨物种高压挤压并格 Job（Stride-3 无锁并行）。
/// 当高压气体 A 顶着低压异种气体 B，且 B 另一侧 C 是真空或与 B 同种气体时，
/// 将 B 推入/合并进 C，腾出 B 格供 A 扩张，解决密闭空间里异种气体无法互相压缩的问题。
/// </summary>
[BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
public struct GasSqueezePassJob : IJobParallelFor
{
    private const float MinSourceMassKg = 0.04f;        // 施压源最小质量（40g）
    private const float PressureRatioThreshold = 1.25f; // A 至少比 B 重 25% 才触发挤压，避免等压附近来回抖动
    private const float MergeOvershootLimit = 1.10f;    // B 并入 C 后质量不得超过 A 的 110%，保证朝等压方向收敛
    private const float ZeroMassEpsilon = 1e-7f;

    [ReadOnly] public NativeArray<ElementDef> ElementDefs;

    // Stride-3 + Phase 保证同一 Pass 内每个三元组 [3t+P, 3t+P+1, 3t+P+2] 绝对互不重叠
    [NativeDisableParallelForRestriction] public NativeArray<ushort> ElementIds;
    [NativeDisableParallelForRestriction] public NativeArray<float> Masses;
    [NativeDisableParallelForRestriction] public NativeArray<float> Temperatures;

    public int Width;
    public int Height;
    public int Axis;
    public int Phase;   // 0, 1, 2
    public int Reverse; // 0 = 先试正向再试反向，1 = 先试反向再试正向

    public void Execute(int i)
    {
        if (ElementDefs.Length == 0)
        {
            return;
        }

        int lineLen = Axis == 0 ? Width - Phase : Height - Phase;
        if (lineLen < 3)
        {
            return;
        }

        int triplesPerLine = lineLen / 3;
        int t = i % triplesPerLine;
        int line = i / triplesPerLine;
        int pos = t * 3 + Phase;

        int idx0;
        int idx1;
        int idx2;

        if (Axis == 0)
        {
            idx0 = line * Width + pos;
            idx1 = idx0 + 1;
            idx2 = idx0 + 2;
        }
        else
        {
            idx0 = pos * Width + line;
            idx1 = idx0 + Width;
            idx2 = idx1 + Width;
        }

        if (Reverse == 0)
        {
            if (!TrySqueeze(idx0, idx1, idx2))
            {
                TrySqueeze(idx2, idx1, idx0);
            }
        }
        else
        {
            if (!TrySqueeze(idx2, idx1, idx0))
            {
                TrySqueeze(idx0, idx1, idx2);
            }
        }
    }

    private bool TrySqueeze(int a, int b, int c)
    {
        ushort idA = ElementIds[a];
        ushort idB = ElementIds[b];
        ushort idC = ElementIds[c];

        if (idA == idB || idA >= ElementDefs.Length || idB >= ElementDefs.Length || idC >= ElementDefs.Length)
        {
            return false;
        }

        ElementDef defA = ElementDefs[idA];
        ElementDef defB = ElementDefs[idB];
        if (defA.State != (byte)ElementState.Gas || defB.State != (byte)ElementState.Gas)
        {
            return false;
        }

        float mA = Masses[a];
        float mB = Masses[b];
        if (mA < MinSourceMassKg || mB <= ZeroMassEpsilon)
        {
            return false;
        }

        if (mA <= mB * PressureRatioThreshold)
        {
            return false;
        }

        ElementDef defC = ElementDefs[idC];
        float tA = Temperatures[a];
        float tB = Temperatures[b];

        // 情况 A：C 是真空，把 B 整格推进 C，A 均分一半扩张进 B
        if (defC.State == (byte)ElementState.Vacuum)
        {
            ElementIds[c] = idB;
            Masses[c] = mB;
            Temperatures[c] = tB;

            float dm = mA * 0.5f;
            ElementIds[b] = idA;
            Masses[b] = dm;
            Temperatures[b] = tA;
            Masses[a] = mA - dm;
            return true;
        }

        // 情况 B：C 与 B 同种气体，将 B 压缩合并入 C（严格守恒质量与能量），A 扩入 B
        if (idC == idB)
        {
            float mC = Masses[c];
            float newMassC = mC + mB;
            if (newMassC > mA * MergeOvershootLimit)
            {
                return false;
            }

            float tC = Temperatures[c];
            Temperatures[c] = (mC * tC + mB * tB) / newMassC;
            Masses[c] = newMassC;

            float dm = mA * 0.5f;
            ElementIds[b] = idA;
            Masses[b] = dm;
            Temperatures[b] = tA;
            Masses[a] = mA - dm;
            return true;
        }

        return false;
    }
}