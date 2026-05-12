using System.Text.Json.Serialization;

namespace _2dTileExporter.Models;

public class ObjectExportData
{
    [JsonPropertyName("objects")]
    public List<MapObject> Objects { get; set; } = [];
}

public class MapObject
{
    [JsonPropertyName("typeName")]
    public string TypeName { get; set; } = "";

    [JsonPropertyName("category")]
    public double Category { get; set; }

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }

    [JsonPropertyName("altitude")]
    public double Altitude { get; set; }

    [JsonPropertyName("heading")]
    public double Heading { get; set; }

    [JsonPropertyName("height")]
    public double? Height { get; set; }

    [JsonPropertyName("footprint")]
    public List<DcsPoint>? Footprint { get; set; }

    [JsonPropertyName("bboxSource")]
    public string? BboxSource { get; set; }
}

public class DcsPoint
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }
}

/// <summary>
/// An object after coordinate conversion to WGS84.
/// </summary>
public sealed class GeoObject(MapObject source, double lat, double lon, (double lon, double lat)[]? footprintRing)
{
    public MapObject Source { get; } = source;
    public double Lat { get; } = lat;
    public double Lon { get; } = lon;
    /// <summary>Exterior ring in (lon, lat) pairs; null when no footprint.</summary>
    public (double lon, double lat)[]? FootprintRing { get; } = footprintRing;
}
