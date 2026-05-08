using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace _2dTileExporter.Readers;

public static class TerrainMetadataReader
{
    public static TerrainMetadata ReadFromFile(string filePath)
    {
        string jsonContent = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<TerrainMetadata>(jsonContent)
            ?? throw new Exception($"Failed to deserialize terrain metadata from file {filePath}");
    }

    public static GlobalTerrainMetadata ReadGlobalFromFile(string filePath)
    {
        string jsonContent = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<GlobalTerrainMetadata>(jsonContent)
            ?? throw new Exception($"Failed to deserialize global terrain metadata from file {filePath}");
    }

    public static DetailedTerrainMetadata ReadDetailedFromFile(string filePath)
    {
        string jsonContent = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<DetailedTerrainMetadata>(jsonContent)
            ?? throw new Exception($"Failed to deserialize detailed terrain metadata from file {filePath}");
    }
}

public class TerrainMetadata
{
    [JsonPropertyName("tileIndex")]
    public int TileIndex { get; set; }

    [JsonPropertyName("tileSamples")]
    public int TileSamples { get; set; }

    [JsonPropertyName("tileSize")]
    public int TileSize { get; set; }

    [JsonPropertyName("tileOrigin")]
    public TileOrigin TileOrigin { get; set; } = new();

    [JsonPropertyName("sampleInterval")]
    public int SampleInterval { get; set; }
}

public class TileOrigin
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}

public class GlobalTerrainMetadata
{
    [JsonPropertyName("topLeft")]
    public TileOrigin TopLeft { get; set; } = new();

    [JsonPropertyName("bottomRight")]
    public TileOrigin BottomRight { get; set; } = new();

    [JsonPropertyName("sampleInterval")]
    public int SampleInterval { get; set; }

    [JsonPropertyName("tileSize")]
    public int TileSize { get; set; }

    [JsonPropertyName("tileSamples")]
    public int TileSamples { get; set; }

    [JsonPropertyName("heightMapInterval")]
    public int HeightMapInterval { get; set; }

    [JsonPropertyName("heightMapSamples")]
    public int HeightMapSamples { get; set; }
}

public class DetailedTerrainMetadata
{
    [JsonPropertyName("topLeft")]
    public TileOrigin TopLeft { get; set; } = new();

    [JsonPropertyName("bottomRight")]
    public TileOrigin BottomRight { get; set; } = new();

    [JsonPropertyName("sampleInterval")]
    public int SampleInterval { get; set; }
}
