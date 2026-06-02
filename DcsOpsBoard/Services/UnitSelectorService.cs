using System;
using DcsMissionParser.Net.Objects.Coalitions.Countries.Groups;
using DcsOpsBoard.Components.PlanningComponents.ShapeTypes;

namespace DcsOpsBoard.Services;

public interface ISelectorService
{
    public event Func<ObjectSelectedArgs, Task> OnObjectSelected;
    public Task SelectRefId(Guid refId, DcsShapeType shapeType);
    public Task UnselectAll();
}

public class ObjectSelectedArgs
{
    public required Guid SelectedObjectRefId { get; set; }
    public required DcsShapeType SelectedObjectType { get; set; }
}

public class SelectorService : ISelectorService
{
    public event Func<ObjectSelectedArgs, Task> OnObjectSelected = (_) => Task.CompletedTask;

    public async Task SelectRefId(Guid refId, DcsShapeType shapeType)
    {
        if(OnObjectSelected != null)
        {
            await OnObjectSelected.Invoke(new ObjectSelectedArgs
            {
                SelectedObjectRefId = refId,
                SelectedObjectType = shapeType
            });
        }
    }

    public async Task UnselectAll()
    {
        if(OnObjectSelected != null)
        {
            await OnObjectSelected.Invoke(new ObjectSelectedArgs
            {
                SelectedObjectRefId = Guid.Empty,
                SelectedObjectType = DcsShapeType.Unknown
            });
        }
    }
}
