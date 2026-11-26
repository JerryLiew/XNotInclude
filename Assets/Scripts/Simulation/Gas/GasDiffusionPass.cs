using Unity.Jobs;

public sealed class GasDiffusionPass : ISimPass
{
    public JobHandle Schedule(SimGridData grid, float dt, int tickIndex, JobHandle dependency)
    {
        JobHandle handle = dependency;

        // 阶段 1：4-Pass 奇偶配对（同种均压、真空扩张、热量平流、重力分层、横向扩散）
        if ((tickIndex & 1) == 0)
        {
            handle = SchedulePairPass(grid, dt, 0, 0, tickIndex, handle);
            handle = SchedulePairPass(grid, dt, 0, 1, tickIndex, handle);
            handle = SchedulePairPass(grid, dt, 1, 0, tickIndex, handle);
            handle = SchedulePairPass(grid, dt, 1, 1, tickIndex, handle);
        }
        else
        {
            handle = SchedulePairPass(grid, dt, 1, 1, tickIndex, handle);
            handle = SchedulePairPass(grid, dt, 1, 0, tickIndex, handle);
            handle = SchedulePairPass(grid, dt, 0, 1, tickIndex, handle);
            handle = SchedulePairPass(grid, dt, 0, 0, tickIndex, handle);
        }

        // 阶段 2：Stride-3 跨物种高压挤压并格（按 tickIndex 轮转相位与轴序，消除方向偏置）
        int startPhase = tickIndex % 3;
        int reverse = tickIndex & 1;

        if (reverse == 0)
        {
            for (int k = 0; k < 3; k++)
            {
                handle = ScheduleSqueezePass(grid, 0, (startPhase + k) % 3, reverse, handle);
            }
            for (int k = 0; k < 3; k++)
            {
                handle = ScheduleSqueezePass(grid, 1, (startPhase + k) % 3, reverse, handle);
            }
        }
        else
        {
            for (int k = 2; k >= 0; k--)
            {
                handle = ScheduleSqueezePass(grid, 1, (startPhase + k) % 3, reverse, handle);
            }
            for (int k = 2; k >= 0; k--)
            {
                handle = ScheduleSqueezePass(grid, 0, (startPhase + k) % 3, reverse, handle);
            }
        }

        return handle;
    }

    private static JobHandle SchedulePairPass(
        SimGridData grid, float dt, int axis, int parity, int tickIndex, JobHandle dep)
    {
        int divisor = axis == 0 ? grid.Width - parity : grid.Height - parity;
        if (divisor <= 1)
        {
            return dep;
        }

        int pairsPerLine = divisor / 2;
        int pairCount = axis == 0 ? pairsPerLine * grid.Height : pairsPerLine * grid.Width;

        var job = new GasPairPassJob
        {
            Width = grid.Width,
            Height = grid.Height,
            Dt = dt,
            Axis = axis,
            Parity = parity,
            TickIndex = tickIndex,
            ElementDefs = grid.ElementDefs,
            ElementIds = grid.ElementIds,
            Masses = grid.Masses,
            Temperatures = grid.Temperatures
        };

        return job.Schedule(pairCount, 64, dep);
    }

    private static JobHandle ScheduleSqueezePass(
        SimGridData grid, int axis, int phase, int reverse, JobHandle dep)
    {
        int lineLen = axis == 0 ? grid.Width - phase : grid.Height - phase;
        if (lineLen < 3)
        {
            return dep;
        }

        int triplesPerLine = lineLen / 3;
        int tripleCount = axis == 0 ? triplesPerLine * grid.Height : triplesPerLine * grid.Width;

        var job = new GasSqueezePassJob
        {
            Width = grid.Width,
            Height = grid.Height,
            Axis = axis,
            Phase = phase,
            Reverse = reverse,
            ElementDefs = grid.ElementDefs,
            ElementIds = grid.ElementIds,
            Masses = grid.Masses,
            Temperatures = grid.Temperatures
        };

        return job.Schedule(tripleCount, 64, dep);
    }
}