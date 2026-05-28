using System;
using DcsMissionParser.Net.Objects.Coalitions.Countries.Groups;
using DcsOpsBoard.Types.Enums;
using OpenLayers.Blazor;

namespace DcsOpsBoard.Components.PlanningComponents.HelperClasses;

public class MissionRenderState
{
    private readonly Dictionary<Guid, Shape> _drawnShapes = [];
    private readonly Dictionary<Guid, PlaneGroup> _drawnFlights = [];
    private readonly Dictionary<Guid, CoalitionSide> _flightCoalitions = [];

    public void AddShape(Guid shapeId, Shape shape)
    {
        _drawnShapes[shapeId] = shape;
    }

    public bool TryGetShape(Guid shapeId, out Shape? shape)
    {
        return _drawnShapes.TryGetValue(shapeId, out shape);
    }

    public void AddFlight(Guid flightId, PlaneGroup group, CoalitionSide coalition)
    {
        _drawnFlights[flightId] = group;
        _flightCoalitions[flightId] = coalition;
    }

    public bool TryGetFlight(Guid flightId, out PlaneGroup? group)
    {
        return _drawnFlights.TryGetValue(flightId, out group);
    }

    public bool TryGetFlightCoalition(Guid flightId, out CoalitionSide coalition)
    {
        return _flightCoalitions.TryGetValue(flightId, out coalition);
    }
     
    public void Clear()
    {
        _drawnShapes.FirstOrDefault().Value?.Map?.ShapesList.Clear();
        _drawnShapes.Clear();
        _drawnFlights.Clear();
        _flightCoalitions.Clear();
    }
}
