
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

namespace DcsOpsBoard.MissionEditing.RenderExtensions;

public static class FlightExtensions
{
    public static async Task RenderAsync(this PlaneGroup group, CoalitionSide side, RoleType role, List<string> ownedFlights, OpenLayers.Blazor.Map map, MissionRenderState renderState, CoordConverter coordConverter)
    {
        
        Layer? nonEditableLayer = map.LayersList.FirstOrDefault(l => l.Id == MapConstants.NonEditableFlightsLayerId);
        Layer? editableLayer = nonEditableLayer;

        if(role == RoleType.Admin || role == RoleType.Editor || ownedFlights.Contains(group.GroupName))
        {
            editableLayer = map.LayersList.FirstOrDefault(l => l.Id == MapConstants.EditableFlightsLayerId);
        }

        if(nonEditableLayer is null || editableLayer is null)
        {
            return;
        }

        renderState.AddFlight(group.RefId, group, side);
        

        //Render: Units as SVG files. 

        //Render: Route as a line 
        await group.Route.RenderAsync(group.RefId, side, role, ownedFlights.Contains(group.GroupName), nonEditableLayer, renderState, coordConverter);

        //Render: Each waypoint as a point with the waypoint number. (this should be draggable)
        foreach(Point waypoint in group.Route.Points)
        {
            await waypoint.RenderAsync(group.RefId, side, role, ownedFlights.Contains(group.GroupName), editableLayer, renderState, coordConverter);
        }        
    }

    private static async Task RenderAsync(this Route route, Guid flightId,  CoalitionSide side, RoleType role, bool editable, Layer layer, MissionRenderState renderState, CoordConverter coordConverter)
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

        line.Stroke = "rgba(238, 255, 0, 0.77)";

        line.StrokeThickness = 3;

        line.Properties[MapConstants.FlightIdKey] = flightId;
        line.Properties[MapConstants.ItemEditableKey] = editable; //Should probably be false as the route shouldn't be editable

        line.UpdateShape();
        
    }

    private static async Task RenderAsync(this Point waypoint, Guid flightId, CoalitionSide side, RoleType role, bool editable, Layer layer, MissionRenderState renderState, CoordConverter coordConverter)
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
        point.Radius = 5;

        point.Properties["$type"] = MapConstants.ShapeTypes.FlightWaypoint;
        point.Properties[MapConstants.FlightIdKey] = flightId;
        point.Properties[MapConstants.ItemEditableKey] = editable;

        await point.UpdateShape();

    }

    private static async Task RenderAsync(this PlaneUnit unit, CoalitionSide side, RoleType role, bool editable, Layer layer, MissionRenderState renderState, CoordConverter coordConverter)
    {
        //TODO: Render the unit as an svg based plane
    }
}
