using System;
using DcsMissionParser.Net;
using DcsOpsBoard.Database.Entities;
using DcsOpsBoard.Hubs.MissionSync;
using DcsOpsBoard.Services.MissionSync;
using OpenLayers.Blazor;

namespace DcsOpsBoard.Hubs.MissionEditing;

public interface IMissionCommand
{
    Guid MissionId { get; }
    Guid MissionStateId { get; }
    
    public string CommandType { get; }
    ulong UserId { get; }
    DateTime Timestamp { get; }

    public Task<CommandResult> CheckPermissions(List<Permission> missionPermissions);
    public Task<CommandResult> ApplyToMission(DcsMission mission);
    public Task<CommandResult> RenderAsync(Map map, Dictionary<Guid, Shape> renderCache, CoordConverter coordConverter);
}
