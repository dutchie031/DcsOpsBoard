
using DcsMissionParser.Net;
using DcsMissionParser.Net.Objects.Coalitions.Countries.Groups;
using DcsOpsBoard.Constants;
using DcsOpsBoard.Database.Enums;
using DcsOpsBoard.Types.Enums;
using OpenLayers.Blazor;
using Route = DcsMissionParser.Net.Objects.Coalitions.Routes.Plane.Route;
using Point = DcsMissionParser.Net.Objects.Coalitions.Routes.Plane.Point;
using DcsMissionParser.Net.Objects.Coalitions.Units.Plane;
using DcsMissionParser.Net.CoordMapping;
using DcsOpsBoard.Components.PlanningComponents.HelperClasses;
using DcsOpsBoard.MissionEditing.RenderExtensions.Context;

namespace DcsOpsBoard.MissionEditing.RenderExtensions;

public static class FlightExtensions
{
    public static async Task RenderAsync(this PlaneGroup group, DcsRenderContext context)
    {
        
        Layer? nonEditableLayer = context.Map.LayersList.FirstOrDefault(l => l.Id == MapConstants.NonEditableFlightsLayerId);
        Layer? editableLayer = nonEditableLayer;
        
        if(context.EditingClient.CurrentRoleInMission is RoleType.Admin or RoleType.Editor || context.EditingClient.OwnedFlights.Contains(group.GroupName))
        {
            editableLayer = context.Map.LayersList.FirstOrDefault(l => l.Id == MapConstants.EditableFlightsLayerId);
        }

        if(nonEditableLayer is null || editableLayer is null)
        {
            return;
        }

        context.RenderState.AddFlight(group.RefId, group);
        bool selected = context.SelectedRefId == group.RefId;
        CoalitionSide side = await context.MissionCache.GetCoalitionForGroup(context.EditingClient.CurrentMissionData!.MissionId, group.RefId);

        //Render: Units as SVG files. 

        //Render: Route as a line 
        await group.Route.RenderAsync(group.RefId, nonEditableLayer, context.RenderState, context.CoordConverter, selected, side);

        //Render: Each waypoint as a point with the waypoint number. (this should be draggable)
        foreach(Point waypoint in group.Route.Points)
        {
            await waypoint.RenderAsync(group.RefId, editableLayer, context.RenderState, context.CoordConverter, selected, side);
        }        
    }

    private static async Task RenderAsync(this Route route, Guid flightId, Layer layer, MissionRenderState renderState, CoordConverter coordConverter, bool selected, CoalitionSide side)
    {
        if(!renderState.TryGetShape(route.RefId, out Shape? cachedShape))
        {
            if (layer is null)
            {
                //No drawing layer, can't render.
                return;
            }

            //No cached shape, create a new one and add it to the cache.
            cachedShape = new OpenLayers.Blazor.Line();
            renderState.AddShape(route.RefId, cachedShape);
            layer.ShapesList.Add(cachedShape);
            await layer.UpdateLayer();
        }
        
        if (cachedShape is not OpenLayers.Blazor.Line line)
            return;
        
        line.Points = [.. route.Points.Select(
            p =>
            {
                DcsCoord coord = new() { X = p.X, Y = p.Y };
                var converted = coordConverter.LOtoLL(coord);
                return new Coordinate(converted.Lon, converted.Lat);
            })];

        if(selected)
        {
            line.Stroke = "rgba(238, 255, 0, 0.77)";
        } 
        else if(side == CoalitionSide.Blue)
        {
            line.Stroke = "rgba(16, 95, 243, 0.49)";
        }
        else if(side == CoalitionSide.Red)
        {
            line.Stroke = "rgba(255, 0, 0, 0.77)";
        }
        else
        {
            line.Stroke = "rgba(255, 255, 255, 0.77)";
        }
       

        line.StrokeThickness = selected ? 4 : 2;

        line.Properties[MapConstants.FlightIdKey] = flightId;

        await line.UpdateShape();
    }

    private static async Task RenderAsync(this Point waypoint, Guid flightId, Layer layer, MissionRenderState renderState, CoordConverter coordConverter, bool selected, CoalitionSide side)
    {
        if(!renderState.TryGetShape(waypoint.RefId, out Shape? cachedShape))
        {
            if (layer is null)
            {
                //No drawing layer, can't render.
                return;
            }

            //No cached shape, create a new one and add it to the cache.
            cachedShape = new OpenLayers.Blazor.Point()
            {
                Id = waypoint.RefId.ToString()
            };
            renderState.AddShape(waypoint.RefId, cachedShape);
            layer.ShapesList.Add(cachedShape);
            await layer.UpdateLayer();
        }

        if (cachedShape is not OpenLayers.Blazor.Point point)
             return;

        DcsCoord coord = new() { X = waypoint.X, Y = waypoint.Y };
        var converted = coordConverter.LOtoLL(coord);
        point.Coordinate = new Coordinate(converted.Lon, converted.Lat);
        point.Stroke = "rgba(238, 255, 0, 0.77)";
        point.Radius = selected ? 7 : 2;

        point.Properties[MapConstants.TypeKey] = MapConstants.ShapeTypes.FlightWaypoint;
        point.Properties[MapConstants.FlightIdKey] = flightId;

        await point.UpdateShape();

    }

    private static async Task RenderAsync(this PlaneUnit unit, CoalitionSide side, RoleType role, bool editable, Layer layer, MissionRenderState renderState, CoordConverter coordConverter)
    {
        //TODO: Render the unit as an svg based plane
    }
}
