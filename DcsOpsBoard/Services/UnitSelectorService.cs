using System;
using DcsMissionParser.Net.Objects.Coalitions.Countries.Groups;

namespace DcsOpsBoard.Services;

public interface IUnitSelectorService
{
    public event Func<PlaneGroup, Task> OnPlaneGroupSelected;

    public Task SelectPlaneGroup(PlaneGroup group);
}

public class UnitSelectorService : IUnitSelectorService
{
    public event Func<PlaneGroup, Task> OnPlaneGroupSelected = (_) => Task.CompletedTask;

    public async Task SelectPlaneGroup(PlaneGroup group)
    {
        if(OnPlaneGroupSelected != null)
        {
            await OnPlaneGroupSelected.Invoke(group);
        }
    }
}
