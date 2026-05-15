using System;
using DcsMissionParser.Net;
using DcsOpsBoard.Database.Entities;
using DcsOpsBoard.Services.MissionSync;
using OpenLayers.Blazor;

namespace DcsOpsBoard.Hubs.MissionEditing.UpdateCommands;

public record AddFlight(
    Guid MissionId,
    ulong UserId, 
    DateTime Timestamp,
    string FlightName
) : IMissionCommand
{
    public string CommandType => nameof(AddFlight);

    public Task<CommandResult> CheckPermissions(List<Permission> missionPermissions)
    {
        throw new NotImplementedException();
    }

    public Task<CommandResult> ApplyToMission(DcsMission mission)
    {
        throw new NotImplementedException();
    }

    public Task<CommandResult> ApplyToMissionMap(Map map)
    {
        throw new NotImplementedException();
    }
}
