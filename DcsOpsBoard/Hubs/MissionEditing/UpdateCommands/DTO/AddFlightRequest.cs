using System;
using DcsMissionParser.Net.CoordMapping;
using DcsMissionParser.Net.Objects.Coalitions.Countries.Groups;
using DcsMissionParser.Net.Objects.Coalitions.Units.Plane;
using DcsOpsBoard.Types.Enums;

namespace DcsOpsBoard.Hubs.MissionEditing.UpdateCommands.DTO;

public class AddFlightRequest
{
    public required string FlightName { get; set; }
    public required CoalitionSide Coalition { get; set; }
    public required PlaneType PlaneType { get; set; }
    public required PlaneTasking Tasking { get; set; }
    public required LatLong Position { get; set; }
}
