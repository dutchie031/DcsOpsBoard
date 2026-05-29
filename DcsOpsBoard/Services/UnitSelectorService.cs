using System;
using DcsMissionParser.Net.Objects.Coalitions.Countries.Groups;

namespace DcsOpsBoard.Services;

public interface IUnitSelectorService
{
    public event Func<UnitSelectedArgs, Task> OnGroupSelected;

    public Task SelectPlaneGroup(PlaneGroup group);
}

public class UnitSelectedArgs
{
    public required Type SelectedType { get; set; }
    public required object SelectedUnit { get; set; }
}

public class UnitSelectorService : IUnitSelectorService
{
    public event Func<UnitSelectedArgs, Task> OnGroupSelected = (_) => Task.CompletedTask;

    public async Task SelectPlaneGroup(PlaneGroup group)
    {
        if(OnGroupSelected != null)
        {
            await OnGroupSelected.Invoke(new UnitSelectedArgs
            {
                SelectedType = typeof(PlaneGroup),
                SelectedUnit = group
            });
        }
    }
}
