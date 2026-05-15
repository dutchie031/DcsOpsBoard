using System;
using System.Collections.Concurrent;
using DcsOpsBoard.Hubs.MissionSync;
using Microsoft.AspNetCore.SignalR;

namespace DcsOpsBoard.Hubs.MissionEditing;

public class MissionEditHub : Hub
{
    private readonly IMissionCache _cache;
    private readonly MissionCommandQueue _commandQueue;

    public MissionEditHub(IMissionCache cache, MissionCommandQueue commandQueue)
    {
        _cache = cache;
        _commandQueue = commandQueue;
    }

    private static readonly ConcurrentDictionary<string, Guid> _connectionMissions = [];

    /// <summary>
    /// Clients join a group for the mission they want to edit. 
    /// </summary>
    /// <param name="missionId"></param>
    /// <returns></returns>
    public async Task JoinMission(Guid missionId)
    {
        //Make sure it's not in another group
        if(_connectionMissions.TryGetValue(Context.ConnectionId, out Guid currentMissionId))
        {
            if(currentMissionId == missionId)
                return; // Already in the correct group
            
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, 
                MissionGroupName(currentMissionId));
        }

        // Verify permissions
        await Groups.AddToGroupAsync(Context.ConnectionId, 
            MissionGroupName(missionId));
        
        // Update the connection's current mission
        _connectionMissions[Context.ConnectionId] = missionId;

        // Send current mission state to new joiner
        var mission = await _cache.GetMission(missionId);
        await Clients.Caller.SendAsync("MissionStateSync", mission);
    }

    public async Task RequestFullUpdate(Guid missionId)
    {
        var mission = await _cache.GetMission(missionId);
        await Clients.Caller.SendAsync("MissionStateSync", mission);
    }


    public async Task SendCommand(IMissionCommand command)
    {
        // Validate command here if needed (e.g. check user permissions)
        
        //Queue the command for processing and await the result
        var result = await _commandQueue.EnqueueCommand(command);
    
        await Clients
            .Group(MissionGroupName(command.MissionId))
            .SendAsync("CommandResult", result);

    }


    private string MissionGroupName(Guid missionId) => $"mission-{missionId}";

}
