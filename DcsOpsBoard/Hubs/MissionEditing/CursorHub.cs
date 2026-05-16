using System;
using System.Collections.Concurrent;
using DcsOpsBoard.Database.Enums;
using DcsOpsBoard.Database.Services;
using DcsOpsBoard.Hubs.HubModels;
using Microsoft.AspNetCore.SignalR;

namespace DcsOpsBoard.Hubs.MissionEditing;

public class CursorHub : Hub, IBaseHub
{
    public static string ReceivedCursorPositionMethodName = "ReceiveCursorPosition";

    private readonly IPermissionManager _permissionManager;

    private static readonly ConcurrentDictionary<string, Guid> _connectionMissions = [];
    private static readonly ConcurrentDictionary<string, RoleType> _connectionRoles = [];

    public static string HubUrl => "/hubs/cursorhub";

    public CursorHub(IPermissionManager permissionManager)
    {
        _permissionManager = permissionManager;
    }

    public async Task JoinCursorGroup(Guid missionId, string userId)
    {
        if (!ulong.TryParse(userId, out ulong parsedUserId))
        {
            return;
        }

        // Make sure user is not in another group.
        if (_connectionMissions.TryGetValue(Context.ConnectionId, out Guid currentMissionId))
        {
            if (currentMissionId == missionId)
            {
                return;
            }

            if (_connectionRoles.TryGetValue(Context.ConnectionId, out RoleType currentRole))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, CursorGroupName(currentMissionId, currentRole));
            }
        }

        // Verify permissions.
        var highest = _permissionManager
            .GetPermissionsForMission(missionId, parsedUserId)
            .Where(x => x.Role != RoleType.Unknown)
            .Select(x => x.Role)
            .DefaultIfEmpty(RoleType.Unknown)
            .Min();

        if (highest == RoleType.Unknown || highest == RoleType.Viewer)
        {
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, CursorGroupName(missionId, highest));
        _connectionMissions[Context.ConnectionId] = missionId;
        _connectionRoles[Context.ConnectionId] = highest;
    }

    public async Task SendCursorPosition(Guid missionId, CursorData cursorData)
    {
        if (!_connectionRoles.TryGetValue(Context.ConnectionId, out RoleType role))
        {
            return;
        }

        if (!_connectionMissions.TryGetValue(Context.ConnectionId, out Guid joinedMissionId) || joinedMissionId != missionId)
        {
            return;
        }

        if (role == RoleType.Viewer)
        {
            return;
        }

        foreach (RoleType receiverRole in GetCursorReceiverRoles(role))
        {
            await Clients.Group(CursorGroupName(missionId, receiverRole)).SendAsync(ReceivedCursorPositionMethodName, cursorData);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (_connectionMissions.TryRemove(Context.ConnectionId, out Guid missionId)
            && _connectionRoles.TryRemove(Context.ConnectionId, out RoleType role))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, CursorGroupName(missionId, role));
        }

        await base.OnDisconnectedAsync(exception);
    }

    private static string CursorGroupName(Guid missionId, RoleType usersRoleType) => $"cursor-mission-{missionId}-{usersRoleType}";

    public List<RoleType> GetCursorReceiverRoles(RoleType senderRole)
    {
        List<RoleType> roles = [];
        switch (senderRole)
        {
            case RoleType.Admin:
                roles.Add(RoleType.Admin);
                roles.Add(RoleType.Editor);
                roles.Add(RoleType.Viewer);
                break;
            case RoleType.Editor:
                roles.Add(RoleType.Editor);
                roles.Add(RoleType.Viewer);
                break;
            case RoleType.Viewer:
                roles.Add(RoleType.Viewer);
                break;
        }

        return roles;
    }
}
