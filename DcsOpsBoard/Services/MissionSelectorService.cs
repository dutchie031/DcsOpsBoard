using System;
using DcsMissionParser.Net;
using DcsOpsBoard.Database.Entities;
using DcsOpsBoard.Types;

namespace DcsOpsBoard.Services;

public interface IMissionSelectorService
{
    event Func<OpsPlanningMission, Task>? OnMissionSelected;
    Task SelectMission(OpsPlanningMission mission);
}

public class MissionSelectorService : IMissionSelectorService
{
    public event Func<OpsPlanningMission, Task>? OnMissionSelected;
    public async Task SelectMission(OpsPlanningMission mission)
    {
        if (OnMissionSelected is not null)
        {
            await OnMissionSelected.Invoke(mission);
        }
    }
}
