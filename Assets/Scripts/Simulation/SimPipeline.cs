using System.Collections.Generic;
using Unity.Jobs;

public sealed class SimPipeline
{
    private readonly List<ISimPass> _passes = new List<ISimPass>();

    public int TickIndex { get; private set; }

    public SimPipeline AddPass(ISimPass pass)
    {
        _passes.Add(pass);
        return this;
    }

    public JobHandle Step(SimGridData grid, float dt, JobHandle dependency = default)
    {
        JobHandle handle = dependency;

        for (int i = 0; i < _passes.Count; i++)
        {
            handle = _passes[i].Schedule(grid, dt, TickIndex, handle);
        }

        TickIndex++;
        return handle;
    }
}
