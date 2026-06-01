using System;
using DcsMissionParser.Net.Objects.Coalitions.Countries.Groups;
using DcsOpsBoard.Types.Enums;
using OpenLayers.Blazor;

namespace DcsOpsBoard.Components.PlanningComponents.HelperClasses;

public class MissionRenderState
{
    private readonly Dictionary<Guid, Shape> _drawnShapes = [];
    private readonly Dictionary<Guid, PlaneGroup> _drawnFlights = [];

    public void AddShape(Guid shapeId, Shape shape)
    {
        _drawnShapes[shapeId] = shape;
    }

    public bool TryGetShape(Guid shapeId, out Shape? shape)
    {
        return _drawnShapes.TryGetValue(shapeId, out shape);
    }

    public void AddFlight(Guid flightId, PlaneGroup group)
    {
        _drawnFlights[flightId] = group;
    }

    public bool TryGetFlight(Guid flightId, out PlaneGroup? group)
    {
        return _drawnFlights.TryGetValue(flightId, out group);
    }

    public void Clear()
    {
        _drawnShapes.FirstOrDefault().Value?.Map?.ShapesList.Clear();
        _drawnShapes.Clear();
        _drawnFlights.Clear();
    }
}
