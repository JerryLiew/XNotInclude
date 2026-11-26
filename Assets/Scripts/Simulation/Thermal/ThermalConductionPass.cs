using Unity.Jobs;

public sealed class ThermalConductionPass : ISimPass
{
    public JobHandle Schedule(SimGridData grid, float dt, int tickIndex, JobHandle dependency)
    {
        JobHandle handle = dependency;

        handle = ScheduleAxisPass(grid, dt, 0, 0, handle);
        handle = ScheduleAxisPass(grid, dt, 0, 1, handle);
        handle = ScheduleAxisPass(grid, dt, 1, 0, handle);
        handle = ScheduleAxisPass(grid, dt, 1, 1, handle);

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
