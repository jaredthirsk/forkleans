namespace Forkleans.Journaling;

public interface IStateMachineStorageProvider
{
    IStateMachineStorage Create(IGrainContext grainContext);
}
