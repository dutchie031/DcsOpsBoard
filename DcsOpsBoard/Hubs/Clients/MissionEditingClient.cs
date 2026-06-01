using System;
using System.Text.Json;
using DcsMissionParser.Net;
using DcsOpsBoard.Database.Entities;
using DcsOpsBoard.Database.Enums;
using DcsOpsBoard.Database.Services;
using DcsOpsBoard.MissionEditing;
using DcsOpsBoard.Services;
using Microsoft.AspNetCore.SignalR.Client;

namespace DcsOpsBoard.Hubs.Clients;

public interface IMissionEditingClient
{
    public DcsMission? CurrentMission { get; }
    public OpsPlanningMission? CurrentMissionData { get; }
    public RoleType CurrentRoleInMission { get; }
    public List<string> OwnedFlights { get; }
    public event Func<Task> OnMissionChanged;
    public event Func<Task> OnMissionReceived;
    public event Func<IMissionCommand, Task> OnCommandReceived;

    public Task SendCommand(IMissionCommand command);

}

public class MissionEditingClient : IMissionEditingClient
{
    private readonly IPermissionManager _permissionManager;
    private readonly IMissionSelectorService _missionSelectorService;
    private readonly HubConnectionProvider<MissionEditHub> _missionEditingHubProvider;
    private readonly IUserAuthenticationState _authState;
    private bool _hubEventsRegistered = false;

    public DcsMission? CurrentMission { get; private set; }
    public OpsPlanningMission? CurrentMissionData { get; private set; }
    public RoleType CurrentRoleInMission { get; private set; } = RoleType.Unknown;
    public List<string> OwnedFlights { get; private set; } = [];


    public event Func<Task> OnMissionChanged = () => Task.CompletedTask;
    public event Func<Task> OnMissionReceived = () => Task.CompletedTask;
    public event Func<IMissionCommand, Task> OnCommandReceived = (command) => Task.CompletedTask;
    public MissionEditingClient(IMissionSelectorService missionSelectorService, 
        HubConnectionProvider<MissionEditHub> missionEditingHubProvider, 
        IPermissionManager permissionManager,
        IUserAuthenticationState authState)
    {
        _missionSelectorService = missionSelectorService;
        _missionSelectorService.OnMissionSelected += MissionSelectorService_OnMissionChanged;
        _missionEditingHubProvider = missionEditingHubProvider; 
        _permissionManager = permissionManager;
        _authState = authState;
    }

    public async Task SendCommand(IMissionCommand command)
    {
        if (CurrentMissionData == null || command.MissionId != CurrentMissionData.MissionId) return;
        
        await _missionEditingHubProvider.EnsureStartedAsync();
        if(_missionEditingHubProvider.Connection.State == HubConnectionState.Connected)
        {
            await _missionEditingHubProvider.Connection.SendAsync(MissionEditHub.SendMissionCommandMethodName, command);
        }
    }

    private async Task MissionSelectorService_OnMissionChanged(OpsPlanningMission? obj)
    {
        if(obj == null)
        {
            CurrentMission = null;
            CurrentMissionData = null;
            CurrentRoleInMission = RoleType.Unknown;
            await OnMissionChanged.Invoke();
            return;
        }

        await  _authState.EnsureLoaded();
        if(!_authState.IsAuthenticated)
        {
            //TODO: Redirect for login?
            return; 
        }

        await RegisterHubEvents();
        await _missionEditingHubProvider.EnsureStartedAsync();
        

        CurrentMissionData = obj;

        if(CurrentMissionData.OwnerId == _authState.DiscordId)
        {
            CurrentRoleInMission = RoleType.Admin;
        }
        else
        {
            CurrentRoleInMission = _permissionManager
                .GetPermissionsForMission(obj.MissionId, _authState.DiscordId)
                .Where(p => p.MissionId == obj.MissionId && p.UserId == _authState.DiscordId) //Additional filtering just in case
                .Select(p => p.Role)
                .GetHighestRole();
        }

        await OnMissionChanged.Invoke();

        if(_missionEditingHubProvider.Connection.State == HubConnectionState.Connected)
        {
            await _missionEditingHubProvider.Connection.SendAsync(MissionEditHub.JoinMethodName, obj.MissionId);
        }
    }


    private async Task RegisterHubEvents()
    {
        if (_hubEventsRegistered) return;

        _missionEditingHubProvider.Connection.On<JsonElement>(MissionEditHub.OnFullUpdate, MissionReceived);
        _missionEditingHubProvider.Connection.On<IMissionCommand>(MissionEditHub.OnMissionUpdate, CommandReceived);

        _hubEventsRegistered = true;
    }

    private async Task MissionReceived(JsonElement payload)
    {
        try
        {
            var mission = payload.Deserialize<DcsMission>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (mission == null)
            {
                Console.WriteLine("ReceiveMissionUpdate received but deserialized mission is null.");
                return;
            }

            CurrentMission = mission;
            await OnMissionReceived.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ReceiveMissionUpdate deserialize failed: {ex}");
            Console.WriteLine($"ReceiveMissionUpdate payload: {payload.GetRawText()}");
        }
    }

    private async Task CommandReceived(IMissionCommand command)
    {
        if (CurrentMissionData == null || command.MissionId != CurrentMissionData.MissionId) return;

        await OnCommandReceived.Invoke(command);
    }
    

}
