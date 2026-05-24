using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using _2dTileExporter.Models;
using _2dTileExporter.Readers;
using DcsMissionParser.Net;
using DcsMissionParser.Net.CoordMapping;
using DcsOpsBoard.Types;
using Mapbox.Vector.Tile;

namespace _2dTileExporter.Exporters;

public class ObjectExporter(Map map, int minZoom, int maxZoom, string sourceFile, string outputDirectory)
{
    private int processed;
    private int totalTiles;
    private DateTime startTime;

    private CoordConverter coordConverter = CoordConverter.CreateForMap(map.ToMapString());

    public async Task Export()
    {
        processed = 0;
        startTime = DateTime.Now;

        Console.WriteLine($"Reading object source file: {sourceFile}");
        List<MapObject> objects = ObjectSourceFileReader.ReadObjects(sourceFile);
        Console.WriteLine($"Loaded {objects.Count} objects.");

        Directory.CreateDirectory(outputDirectory);

        for (int zoom = minZoom; zoom <= maxZoom; zoom++)
        {


            Dictionary<(int x, int y), TileData> tiles = [];
            int invalid = 0;
            foreach (var obj in objects)
            {
                if (obj.Footprint == null || obj.Footprint.Count < 3)
                {
                    invalid++;
                    continue;
                }

                HashSet<(int tileX, int tileY)> tilesCoveredByObject = [];
                foreach (var point in obj.Footprint)
                {
                    LatLong coord = coordConverter.LOtoLL(new() { X = point.X, Y = point.Z });
                    int tileX = LonToTileX(coord.Lon, zoom);
                    int tileY = LatToTileY(coord.Lat, zoom);
                    var key = (tileX, tileY);
                    tilesCoveredByObject.Add(key);
                }

                foreach (var tile in tilesCoveredByObject)
                {
                    if (!tiles.TryGetValue(tile, out TileData? value))
                    {
                        value = new()
                        {
                            MinLat = TileYToLat(tile.tileY + 1, zoom),
                            MaxLat = TileYToLat(tile.tileY, zoom),
                            MinLon = TileXToLon(tile.tileX, zoom),
                            MaxLon = TileXToLon(tile.tileX + 1, zoom)
                        };
                        tiles[tile] = value;
                    }

                    value.Objects.Add(obj);
                }
            }

            foreach (var tile in tiles)
            {
                await WriteTile(zoom, tile.Key.x, tile.Key.y, tile.Value);
            }
        }
        CreateCoverageJson(outputDirectory);
    }

    private static ulong ObjectId = 0;
    private async Task WriteTile(int z, int x, int y, TileData tileData)
    {
        // Implementation for writing a tile
        uint extent = 4096;
        var layer = new VectorTileLayer("layer", 2, extent);

        foreach (var obj in tileData.Objects)
        {
            List<Coordinate> ring = [];
            foreach (var point in obj.Footprint!)
            {
                LatLong coord = coordConverter.LOtoLL(new() { X = point.X, Y = point.Z });

                double normalizedX = (coord.Lon - tileData.MinLon) / (tileData.MaxLon - tileData.MinLon);
                double normalizedY = (tileData.MaxLat - coord.Lat) / (tileData.MaxLat - tileData.MinLat);

                int tileX = (int)Math.Clamp(normalizedX * extent, 0, (int)extent - 1);
                int tileY = (int)Math.Clamp(normalizedY * extent, 0, (int)extent - 1);
                ring.Add(new Coordinate(tileX, tileY));
            }

            var fixedRing = ValidateAndFixRing(ring);
            if (fixedRing == null)
            {
                continue;
            }
            List<ArraySegment<Coordinate>> geometry = [fixedRing.ToArray()];

            var feature = new VectorTileFeature(
                Interlocked.Increment(ref ObjectId).ToString(),
                geometry,
                [
                    new ("Type", obj.TypeName ?? string.Empty),
                ],
                Tile.GeomType.Polygon,
                extent
            );

            layer.VectorTileFeatures.Add(feature);
        }

        string outputPath = Path.Combine(outputDirectory, z.ToString(), x.ToString(), $"{y}.mvt");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        List<VectorTileLayer> layers = [layer];
        using FileStream fs = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        VectorTileEncoder.Encode(layers, fs);
    }

    public static void CreateCoverageJson(string outputPath)
    {
        Dictionary<string, List<string>> coverage = new();

        foreach (var file in Directory.EnumerateFiles(outputPath, "*.mvt", SearchOption.AllDirectories))
        {
            string[] parts = file.Split(Path.DirectorySeparatorChar);
            int z = int.Parse(parts[^3]);
            int x = int.Parse(parts[^2]);
            int y = int.Parse(Path.GetFileNameWithoutExtension(parts[^1]));

            if (!coverage.TryGetValue(z.ToString(), out List<string>? tiles))
            {
                tiles = [];
                coverage[z.ToString()] = tiles;
            }
            tiles.Add($"{x}:{y}");
        }

        string json = JsonSerializer.Serialize(coverage, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(outputPath, "coverage.json"), json);
    }

    private static int LonToTileX(double lonDeg, int z)
    {
        double n = 1 << z;
        return (int)Math.Floor((lonDeg + 180.0) / 360.0 * n);
    }

    private static int LatToTileY(double latDeg, int z)
    {
        double latRad = latDeg * Math.PI / 180.0;
        double n = 1 << z;
        double y = (1.0 - Math.Log(Math.Tan(latRad) + (1.0 / Math.Cos(latRad))) / Math.PI) / 2.0 * n;
        return (int)Math.Floor(y);
    }

    public static double TileXToLon(int x, int z)
    {
        double n = 1 << z;
        return (x / n) * 360.0 - 180.0;
    }

    public static double TileYToLat(int y, int z)
    {
        double n = 1 << z;
        double mercY = Math.PI * (1.0 - 2.0 * y / n);
        return Math.Atan(Math.Sinh(mercY)) * 180.0 / Math.PI;
    }

    private static List<Coordinate>? ValidateAndFixRing(List<Coordinate> ring)
    {
        if (ring.Count < 3)
        {
            return null;
        }

        var cleaned = new List<Coordinate>(ring.Count);

        foreach (var point in ring)
        {
            if (cleaned.Count == 0)
            {
                cleaned.Add(point);
                continue;
            }

            var last = cleaned[^1];
            if (last.X != point.X || last.Y != point.Y)
            {
                cleaned.Add(point);
            }
        }

        if (cleaned.Count < 3)
        {
            return null;
        }

        var first = cleaned[0];
        var lastPoint = cleaned[^1];
        if (first.X != lastPoint.X || first.Y != lastPoint.Y)
        {
            cleaned.Add(new Coordinate(first.X, first.Y));
        }

        if (cleaned.Count < 4)
        {
            return null;
        }

        var area = SignedArea(cleaned);
        if (area == 0)
        {
            return null;
        }

        // Normalize exterior ring winding in tile coordinate space.
        // If this ends up inverted with your encoder, flip the comparison.
        if (area > 0)
        {
            cleaned.Reverse();

            first = cleaned[0];
            lastPoint = cleaned[^1];
            if (first.X != lastPoint.X || first.Y != lastPoint.Y)
            {
                cleaned.Add(new Coordinate(first.X, first.Y));
            }
        }

        return cleaned;
    }


    private static double SignedArea(List<Coordinate> ring)
    {
        double area = 0;

        for (int i = 0; i < ring.Count - 1; i++)
        {
            var a = ring[i];
            var b = ring[i + 1];
            area += (a.X * b.Y) - (b.X * a.Y);
        }

        return area / 2.0;
    }

    private class TileData
    {
        public required double MinLat { get; set; }
        public required double MinLon { get; set; }
        public required double MaxLat { get; set; }
        public required double MaxLon { get; set; }
        public List<MapObject> Objects { get; set; } = [];
    }
}
