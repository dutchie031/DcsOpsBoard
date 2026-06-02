using System;
using DcsMissionParser.Net.Objects.Coalitions.Countries.Groups;
using DcsOpsBoard.Types.Enums;

namespace DcsOpsBoard.Components.Modals.FlightModals;

public interface IFlightModalService
{
    public event Func<PlaneGroup, bool, Task> OnEditFlightModalRequested;
    public event Func<CoalitionSide, bool, Task> OnCreateFlightModalRequested;
    public Task ShowEditFlightModal(PlaneGroup flight, bool isReadOnly);
    public Task ShowCreateFlightModal(CoalitionSide side, bool isClientFlight);
}

public class FlightModalService : IFlightModalService
{
    public event Func<PlaneGroup, bool, Task> OnEditFlightModalRequested = (flight, isReadOnly) => Task.CompletedTask;
    public event Func<CoalitionSide, bool, Task> OnCreateFlightModalRequested = (side, isClientFlight) => Task.CompletedTask;

    public async Task ShowCreateFlightModal(CoalitionSide side, bool isClientFlight)
    {
        await OnCreateFlightModalRequested.Invoke(side, isClientFlight);
    }

    public async Task ShowEditFlightModal(PlaneGroup flight, bool isReadOnly)
    {
        await OnEditFlightModalRequested.Invoke(flight, isReadOnly);
    }
}
