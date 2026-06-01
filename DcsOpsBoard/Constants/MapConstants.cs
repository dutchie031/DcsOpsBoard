using System;

namespace DcsOpsBoard.Constants;

public static class MapConstants
{
    public const string DrawingLayerId = "drawing-layer";
    public const string EditableFlightsLayerId = "editable-flights-layer";
    public const string NonEditableFlightsLayerId = "non-editable-flights-layer";

    public const string ItemSelectableKey = "selectable";
    public const string ItemEditableKey = "editable";

    public const string TypeKey = "$type";

    public const string FlightIdKey = "flight-id";

    public static class ShapeTypes
    {
        public const string FlightWaypoint = "flight-waypoint";
    }

}
