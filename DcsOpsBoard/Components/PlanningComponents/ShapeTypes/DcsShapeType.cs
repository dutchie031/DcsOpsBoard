using DcsMissionParser.Net.CoordMapping;
using DcsMissionParser.Net.Objects.Coalitions.Countries.Groups;
using DcsOpsBoard.Constants;
using DcsOpsBoard.MissionEditing.RenderExtensions.Context;
using DcsOpsBoard.MissionEditing.UpdateCommands.DTO;
using OpenLayers.Blazor;

namespace DcsOpsBoard.Components.PlanningComponents.ShapeTypes;

public enum DcsShapeType
{
    Unknown = 0,
    Waypoint = 1,
    Group = 2
}

public static class DcsShapeTypeExtensions
{
    public static async Task ShapeMoved(this DcsShapeType shapeType, Guid shapeId, double lat, double lon, DcsRenderContext context)
    {
        switch (shapeType)
        {
            case DcsShapeType.Waypoint:
                await HandleWaypointMoved(shapeId, lat, lon, context);
                break;
            default:
                // Handle unknown shape type
                break;
        }
    }

    private static async Task HandleWaypointMoved(Guid shapeId, double lat, double lon, DcsRenderContext context)
    {
        DcsCoord newCoord = context.CoordConverter.LLtoLO(new (lat, lon));
        if(!context.RenderState.TryGetShape(shapeId, out Shape? shape) || shape == null)
        {
            Console.WriteLine($"Shape with id {shapeId} not found");
            return;
        }

        Guid flightId = shape.Properties.TryGetValue(MapConstants.FlightIdKey, out dynamic? value) && value is Guid id ? id : Guid.Empty;
        if(flightId == Guid.Empty)
        {
            Console.WriteLine($"Shape with id {shapeId} does not have a valid flight id");
            return;
        }

        if(!context.RenderState.TryGetFlight(flightId, out PlaneGroup? flight) || flight == null)
        {
            Console.WriteLine($"Flight with id {flightId} not found");
            return;
        }

        if(context.UserId == null)
        {
            Console.WriteLine($"User id is null");
            return;
        }
    
        MoveWaypointData moveData = new (flight.RefId, shapeId, newCoord);
        MoveWaypoint moveCommand = new (context.MissionId, context.MissionStateId, context.UserId.Value, DateTime.UtcNow, moveData);
        
        await context.EditingClient.SendCommand(moveCommand);
    }
}