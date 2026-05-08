using System;

namespace DcsOpsBoard.Types;

public enum TerrainType
{
    UNKNOWN = 0,
    LAND = 1,
    SHALLOW_WATER = 2,
    WATER = 3,
    ROAD = 4,
    RUNWAY = 5,
    // custom additions
    LAND_GRASS = 6,
    LAND_HILLS = 7,
    LAND_ROCKY = 8,
    LAND_TREES = 9,
    LAND_SNOW = 10,

    // Custom additions for town/urban areas
    LAND_CITY = 21,
    LAND_TOWN = 22
}

public static class TerrainTypeExtensions
{
    public static TerrainType ToTerrainType(this byte terrainType)
    {
        return terrainType switch
        {
            1 => TerrainType.LAND,
            2 => TerrainType.SHALLOW_WATER,
            3 => TerrainType.WATER,
            4 => TerrainType.ROAD,
            5 => TerrainType.RUNWAY,
            6 => TerrainType.LAND_GRASS,
            7 => TerrainType.LAND_HILLS,
            8 => TerrainType.LAND_ROCKY,
            9 => TerrainType.LAND_TREES,
            10 => TerrainType.LAND_SNOW,
            21 => TerrainType.LAND_CITY,
            22 => TerrainType.LAND_TOWN,
            _ => TerrainType.UNKNOWN
        };
    }

    public static (int r, int g, int b) ToDefaultColor(this TerrainType terrainType, Map map)
    {
        return terrainType switch
        {
            TerrainType.LAND => (180, 160, 130),
            TerrainType.SHALLOW_WATER => (95, 135, 160),
            TerrainType.WATER => (65, 95, 125),
            TerrainType.ROAD => (85, 80, 75),
            TerrainType.RUNWAY => (70, 68, 65),
            TerrainType.LAND_GRASS => (115, 135, 85),
            TerrainType.LAND_HILLS => (130, 110, 80),
            TerrainType.LAND_ROCKY => (145, 135, 120),
            TerrainType.LAND_TREES => (95, 115, 75),
            TerrainType.LAND_SNOW => (240, 240, 245),
            TerrainType.LAND_CITY => (185, 160, 135),
            TerrainType.LAND_TOWN => (165, 145, 125),
            _ => (0, 0, 0)
        };
    }
}


