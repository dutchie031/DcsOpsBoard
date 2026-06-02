using System;
using System.Collections.Concurrent;
using System.Text.Json;
using DcsOpsBoard.Hubs;
using DcsOpsBoard.Hubs.MissionSync;
using Microsoft.AspNetCore.SignalR;

namespace DcsOpsBoard.MissionEditing;

public class MissionEditHub : Hub, IBaseHub
{
    public static readonly string JoinMethodName = nameof(JoinMission);
    public static readonly string SendMissionCommandMethodName = nameof(SendCommand);
    public static readonly string OnFullUpdate = "MissionStateSync";
    public static readonly string OnMissionUpdate = "ReceiveMissionUpdate";


    private readonly IMissionCache _cache;
    private readonly MissionCommandQueue _commandQueue;
    private readonly ILogger<MissionEditHub> _logger;

    public MissionEditHub(IMissionCache cache, MissionCommandQueue commandQueue, ILogger<MissionEditHub> logger)
    {
        _cache = cache;
        _commandQueue = commandQueue;
        _logger = logger;
    }

    private static readonly ConcurrentDictionary<string, Guid> _connectionMissions = [];

    public static string HubUrl => "/hubs/missionedit";


    /// <summary>
    /// Clients join a group for the mission they want to edit. 
    /// </summary>
    /// <param name="missionId"></param>
    /// <returns></returns>
    public async Task JoinMission(Guid missionId)
    {
        //Make sure it's not in another group
        if (_connectionMissions.TryGetValue(Context.ConnectionId, out Guid currentMissionId))
        {
            if (currentMissionId != missionId)
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId,
                    MissionGroupName(currentMissionId));
            }
        }

        // Verify permissions
        await Groups.AddToGroupAsync(Context.ConnectionId,
            MissionGroupName(missionId));

        // Update the connection's current mission
        _connectionMissions[Context.ConnectionId] = missionId;

        // Send current mission state to new joiner
        var mission = await _cache.GetMission(missionId);
        _logger.LogDebug($"Hub sending mission with state id: {mission?.ParserId}");
        await Clients.Caller.SendAsync(OnFullUpdate, mission);
    }

    public async Task RequestFullUpdate(Guid missionId)
    {
        var mission = await _cache.GetMission(missionId);
        await Clients.Caller.SendAsync(OnFullUpdate, mission);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _connectionMissions.TryRemove(Context.ConnectionId, out _);
        return base.OnDisconnectedAsync(exception);
    }

    public async Task SendCommand(JsonElement commandJson)
    {
        // Validate command here if needed (e.g. check user permissions)
        var command = IMissionCommand.FromJsonElement(commandJson);
        if (command == null)
        {
            _logger.LogWarning("SendCommand: Failed to deserialize command");
            return;
        }

        Console.WriteLine($"MissionState on Hub: {command.MissionStateId}");

        //Queue the command for processing and await the result
        var result = await _commandQueue.EnqueueCommand(command);

        // await Clients
        //     .Group(MissionGroupName(command.MissionId))
        //     .SendAsync("CommandResult", result);

        if (result.Succeeded)
        {
            await Clients
            .GroupExcept(MissionGroupName(command.MissionId), Context.ConnectionId)
            .SendAsync(OnMissionUpdate, commandJson);
        }
    }


    private string MissionGroupName(Guid missionId) => $"mission-{missionId}";

}
