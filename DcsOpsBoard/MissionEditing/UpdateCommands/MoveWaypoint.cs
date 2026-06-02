using DcsMissionParser.Net;
using DcsMissionParser.Net.CoordMapping;
using DcsMissionParser.Net.Objects.Coalitions.Countries.Groups;
using DcsOpsBoard.Database.Entities;
using DcsOpsBoard.MissionEditing.RenderExtensions;
using DcsOpsBoard.MissionEditing.RenderExtensions.Context;
using DcsOpsBoard.Services.MissionSync;
using OpenLayers.Blazor;
using DcsPoint = DcsMissionParser.Net.Objects.Coalitions.Routes.Plane.Point;

namespace DcsOpsBoard.MissionEditing.UpdateCommands.DTO;

public record MoveWaypointData(Guid GroupId, Guid WaypointId, DcsCoord newPosition) { }

public record class MoveWaypoint(
    Guid MissionId,
    Guid MissionStateId,
    ulong UserId,
    DateTime Timestamp,
    MoveWaypointData Request) : IMissionCommand
{
    public string CommandType => nameof(MoveWaypoint);

    public async Task<CommandResult> ApplyToMission(DcsMission mission)
    {
        PlaneGroup? group = mission.Coalitions.All
            .SelectMany(c => c.Countries).SelectMany(c => c.Planes.Groups)
            .FirstOrDefault(g => g.RefId == Request.GroupId);

        if (group == null)
            return CommandResult.Failure($"Group with id {Request.GroupId} not found");

        DcsPoint? waypoint = group.Route.Points.FirstOrDefault(p => p.RefId == Request.WaypointId);
        if (waypoint == null)
        {
            return CommandResult.Failure($"Waypoint with id {Request.WaypointId} not found");
        }

        waypoint.X = Request.newPosition.X;
        waypoint.Y = Request.newPosition.Y;
        return CommandResult.Success();
    }

    public async Task<CommandResult> CheckPermissions(List<Permission> missionPermissions)
    {
        //TODO: Permission check. Should check if user has edit permissions for the flight that this waypoint belongs to.
        return CommandResult.Success();
    }

    public async Task<CommandResult> RenderAsync(DcsRenderContext renderContext)
    {
        if(Request.GroupId == Guid.Empty || renderContext.RenderState.TryGetFlight(Request.GroupId, out PlaneGroup? group) == false || group == null)
        {
            return CommandResult.Failure($"Invalid flight id {Request.GroupId}");
        }

        DcsPoint? point = group.Route.Points.FirstOrDefault(p => p.RefId == Request.WaypointId);
        if(point == null)
        {
            return CommandResult.Failure($"Waypoint with id {Request.WaypointId} not found");
        }

        point.X = Request.newPosition.X;
        point.Y = Request.newPosition.Y;

        await group.RenderAsync(renderContext);
        return CommandResult.Success();
    }
}
