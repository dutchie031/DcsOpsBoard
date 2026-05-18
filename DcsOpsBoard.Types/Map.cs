using System;
using DcsMissionParser.Net;
using DcsMissionParser.Net.CoordMapping;

namespace DcsOpsBoard.Types;


public enum Map
{
    Unknown = 0,
    Caucasus = 1,
    Kola = 2,
    GermanyCW = 3,
}

public static class MapExtensions
{
    public static Map ToMap(this string map)
    {
        return map.ToLower() switch
        {
            "caucasus" => Map.Caucasus,
            "kola" => Map.Kola,
            _ => Map.Unknown
        };
    }

    public static string ToMapString(this Map map)
    {
        return map switch
        {
            Map.Caucasus => "caucasus",
            Map.Kola => "kola",
            _ => "unknown"
        };
    }

    private readonly static Dictionary<Map, CoordConverter> _coordConverters = [];

    extension(Map map)
    {
        public int MaxZoom => map switch
        {
            Map.Caucasus => 17,
            Map.Kola => 17,
            _ => 0
        };

        public int MinZoom => map switch
        {
            Map.Caucasus => 8,
            Map.Kola => 8,
            _ => 0
        };
        

        public LatLong Center => map switch
        {
            Map.Caucasus => new () { Lat = 43.0, Lon = 41.0 },
            Map.Kola => new () { Lat = 69.0, Lon = 31.0 },
            _ => new () { Lat = 0.0, Lon = 0.0 }
        };

        public CoordConverter CoordConverter
        {
            get
            {
                if (_coordConverters.TryGetValue(map, out var converter))
                   return converter;

                CoordConverter newConverter =  CoordConverter.CreateForMap(map.ToMapString());
                _coordConverters[map] = newConverter;
                return newConverter;
            }
        }

        public MapLimits Limits => map switch
        {
            Map.Caucasus => new MapLimits
            {
                BottomLeft = map.CoordConverter.LOtoLL(new() { X = -450000, Y = 0 }),
                TopRight =  map.CoordConverter.LOtoLL(new() { X = 65000, Y = 950000 }),
            },
            Map.Kola => new MapLimits
            {
                BottomLeft = map.CoordConverter.LOtoLL(new() { X = -314667, Y = -671814 }),
                TopRight = map.CoordConverter.LOtoLL(new() { X = 584915, Y = 855667 })
            },
            _ => throw new ArgumentOutOfRangeException(nameof(map), $"No limits defined for map {map}")
        };
    }

}

public class MapLimits
{
    public required LatLong BottomLeft { get; set; }
    public required LatLong TopRight { get; set; }
}