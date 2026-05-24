using System;
using System.Text.Json;
using _2dTileExporter.Models;

namespace _2dTileExporter.Readers;

public class ObjectSourceFileReader
{
    public static List<MapObject> ReadObjects(string filePath)
    {
        using FileStream fileStream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        ObjectExportData? wrapped = JsonSerializer.Deserialize<ObjectExportData>(fileStream);
        if (wrapped?.Objects is { Count: > 0 })
        {
            return wrapped.Objects;
        }

        fileStream.Position = 0;
        List<MapObject>? plainList = JsonSerializer.Deserialize<List<MapObject>>(fileStream);
        return plainList ?? throw new InvalidDataException($"Failed to deserialize object data from file: {filePath}");
    }
}
