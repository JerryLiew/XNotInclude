using Unity.Jobs;

public interface ISimPass
{
    JobHandle Schedule(SimGridData grid, float dt, int tickIndex, JobHandle dependency);
}
