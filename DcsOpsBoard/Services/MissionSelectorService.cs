using System;

namespace DcsOpsBoard.Services;

public interface IMissionSelectorService
{
    event Func<Guid, Task>? OnMissionSelected;
    Task SelectMission(Guid missionId);
}

public class MissionSelectorService : IMissionSelectorService
{
    public event Func<Guid, Task>? OnMissionSelected;
    public async Task SelectMission(Guid missionId)
    {
        if (OnMissionSelected is not null)
        {
            await OnMissionSelected.Invoke(missionId);
        }
    }
}
