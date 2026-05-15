using System;
using DcsMissionParser.Net;
using DcsOpsBoard.Hubs.MissionSync;
using DcsOpsBoard.Services.MissionSync;
using OpenLayers.Blazor;

namespace DcsOpsBoard.Hubs.MissionEditing;

public interface IMissionCommand
{
    Guid MissionId { get; }
    public string CommandType { get; }
    ulong UserId { get; }
    DateTime Timestamp { get; }

    public Task<CommandResult> ApplyToMission(DcsMission mission);
    public Task<CommandResult> ApplyToMissionMap(OpenLayers.Blazor.Map map);
}
