using Unity.Jobs;

public sealed class ThermalConductionPass : ISimPass
{
    public JobHandle Schedule(SimGridData grid, float dt, int tickIndex, JobHandle dependency)
    {
        JobHandle handle = dependency;

        // 偶数 Tick 正序、奇数 Tick 逆序，抵消 4-Pass 串行带来的微小方向偏置
        if ((tickIndex & 1) == 0)
        {
            handle = ScheduleAxisPass(grid, dt, 0, 0, handle); // 横偶
            handle = ScheduleAxisPass(grid, dt, 0, 1, handle); // 横奇
            handle = ScheduleAxisPass(grid, dt, 1, 0, handle); // 纵偶
            handle = ScheduleAxisPass(grid, dt, 1, 1, handle); // 纵奇
        }
        else
        {
            handle = ScheduleAxisPass(grid, dt, 1, 1, handle); // 纵奇
            handle = ScheduleAxisPass(grid, dt, 1, 0, handle); // 纵偶
            handle = ScheduleAxisPass(grid, dt, 0, 1, handle); // 横奇
            handle = ScheduleAxisPass(grid, dt, 0, 0, handle); // 横偶
        }

        return handle;
    }

    private static JobHandle ScheduleAxisPass(SimGridData grid, float dt, int axis, int parity, JobHandle dep)
    {
        int divisor = axis == 0 ? grid.Width - parity : grid.Height - parity;
        if (divisor <= 1)
        {
            return dep;
        }

        int pairsPerLine = divisor / 2;
        int pairCount = axis == 0 ? pairsPerLine * grid.Height : pairsPerLine * grid.Width;

        var job = new ThermalPairPassJob
        {
            Width = grid.Width,
            Height = grid.Height,
            Dt = dt,
            Axis = axis,
            Parity = parity,
            ElementDefs = grid.ElementDefs,
            ElementIds = grid.ElementIds,
            Masses = grid.Masses,
            Temperatures = grid.Temperatures
        };

        return job.Schedule(pairCount, 64, dep);
    }
}