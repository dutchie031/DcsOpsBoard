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
    private bool validatedSampleTile;

    private CoordConverter coordConverter = CoordConverter.CreateForMap(map.ToMapString());

    public async Task Export()
    {
        processed = 0;
        startTime = DateTime.Now;

        Console.WriteLine($"Reading object source file: {sourceFile}");
        List<MapObject> objects = ObjectSourceFileReader.ReadObjects(sourceFile);
        Console.WriteLine($"Loaded {objects.Count} objects.");

        // Correctness-first export: remove stale tiles from previous runs.
        if (Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
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

                var footprint = ProjectFootprint(obj.Footprint);
                if (footprint.Count < 3)
                {
                    invalid++;
                    continue;
                }

                var bounds = GetBounds(footprint);
                HashSet<(int tileX, int tileY)> tilesCoveredByObject = GetIntersectingTiles(bounds, zoom);

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

                    value.Objects.Add(new TileObjectData
                    {
                        Object = obj,
                        Footprint = footprint
                    });
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
            var clippedRing = ClipFootprintToTile(obj.Footprint, tileData, extent);
            if (clippedRing.Count < 3)
            {
                continue;
            }

            List<Coordinate> ring = clippedRing
                .Select(point => new Coordinate(
                    (int)Math.Clamp(Math.Round(point.X), 0, extent),
                    (int)Math.Clamp(Math.Round(point.Y), 0, extent)))
                .ToList();

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
                    new ("Type", obj.Object.TypeName ?? string.Empty),
                ],
                Tile.GeomType.Polygon,
                extent
            );

            layer.VectorTileFeatures.Add(feature);
        }

        string outputPath = Path.Combine(outputDirectory, z.ToString(), x.ToString(), $"{y}.mvt");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        List<VectorTileLayer> layers = [layer];
        using (FileStream fs = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            VectorTileEncoder.Encode(layers, fs);
        }
        

        if (!validatedSampleTile && layer.VectorTileFeatures.Count > 0)
        {
            validatedSampleTile = true;
            ValidateWrittenTile(outputPath, extent);
        }
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

        // MVT polygon rings use tile/screen coordinates where Y grows downward.
        // In that coordinate system, a positive shoelace area is a clockwise
        // exterior ring, which is what vector tiles expect for outers.
        if (area < 0)
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

    private static void ValidateWrittenTile(string outputPath, uint extent)
    {
        using FileStream fileStream = File.OpenRead(outputPath);
        List<VectorTileLayer> layers = VectorTileParser.Parse(fileStream);

        foreach (var layer in layers)
        {
            foreach (var feature in layer.VectorTileFeatures.Where(x => x.GeometryType == Tile.GeomType.Polygon))
            {
                if (feature.Geometry.Count == 0)
                {
                    throw new InvalidDataException($"Polygon feature in '{outputPath}' has no rings.");
                }

                foreach (var ringSegment in feature.Geometry)
                {
                    Coordinate[] coords = ringSegment.ToArray();
                    if (coords.Length < 4)
                    {
                        throw new InvalidDataException($"Polygon feature in '{outputPath}' has too few points.");
                    }

                    Coordinate first = coords[0];
                    Coordinate last = coords[^1];
                    if (first.X != last.X || first.Y != last.Y)
                    {
                        throw new InvalidDataException($"Polygon feature in '{outputPath}' is not closed.");
                    }

                    if (SignedArea(coords.ToList()) == 0)
                    {
                        throw new InvalidDataException($"Polygon feature in '{outputPath}' has zero area.");
                    }

                    foreach (var coord in coords)
                    {
                        if (coord.X < 0 || coord.X > extent || coord.Y < 0 || coord.Y > extent)
                        {
                            throw new InvalidDataException($"Polygon feature in '{outputPath}' has coordinates outside tile extent.");
                        }
                    }
                }
            }
        }
    }

    private static HashSet<(int tileX, int tileY)> GetIntersectingTiles(Bounds bounds, int zoom)
    {
        HashSet<(int tileX, int tileY)> tiles = [];
        int maxIndex = (1 << zoom) - 1;

        int minTileX = Math.Clamp(LonToTileX(bounds.MinLon, zoom), 0, maxIndex);
        int maxTileX = Math.Clamp(LonToTileX(bounds.MaxLon, zoom), 0, maxIndex);
        int topTileY = Math.Clamp(LatToTileY(bounds.MaxLat, zoom), 0, maxIndex);
        int bottomTileY = Math.Clamp(LatToTileY(bounds.MinLat, zoom), 0, maxIndex);

        for (int tileX = Math.Min(minTileX, maxTileX); tileX <= Math.Max(minTileX, maxTileX); tileX++)
        {
            for (int tileY = Math.Min(topTileY, bottomTileY); tileY <= Math.Max(topTileY, bottomTileY); tileY++)
            {
                Bounds tileBounds = new(
                    TileXToLon(tileX, zoom),
                    TileYToLat(tileY + 1, zoom),
                    TileXToLon(tileX + 1, zoom),
                    TileYToLat(tileY, zoom));

                if (BoundsIntersect(bounds, tileBounds))
                {
                    tiles.Add((tileX, tileY));
                }
            }
        }

        return tiles;
    }

    private static bool BoundsIntersect(Bounds left, Bounds right)
    {
        return left.MinLon <= right.MaxLon
            && left.MaxLon >= right.MinLon
            && left.MinLat <= right.MaxLat
            && left.MaxLat >= right.MinLat;
    }

    private static Bounds GetBounds(List<LatLong> footprint)
    {
        return new Bounds(
            footprint.Min(point => point.Lon),
            footprint.Min(point => point.Lat),
            footprint.Max(point => point.Lon),
            footprint.Max(point => point.Lat));
    }

    private List<LatLong> ProjectFootprint(IReadOnlyCollection<DcsPoint> footprint)
    {
        return footprint
            .Select(point => _2dTileExporter.Utils.CoordConversion.SafeLOtoLL(coordConverter, new DcsCoord { X = point.X, Y = point.Z }))
            .ToList();
    }

    private static List<TilePoint> ClipFootprintToTile(List<LatLong> footprint, TileData tileData, uint extent)
    {
        List<TilePoint> polygon = footprint
            .Select(point => new TilePoint(
                ((point.Lon - tileData.MinLon) / (tileData.MaxLon - tileData.MinLon)) * extent,
                ((tileData.MaxLat - point.Lat) / (tileData.MaxLat - tileData.MinLat)) * extent))
            .ToList();

        polygon = ClipPolygonEdge(polygon, p => p.X >= 0, (a, b) => IntersectVertical(a, b, 0));
        polygon = ClipPolygonEdge(polygon, p => p.X <= extent, (a, b) => IntersectVertical(a, b, extent));
        polygon = ClipPolygonEdge(polygon, p => p.Y >= 0, (a, b) => IntersectHorizontal(a, b, 0));
        polygon = ClipPolygonEdge(polygon, p => p.Y <= extent, (a, b) => IntersectHorizontal(a, b, extent));

        return polygon;
    }

    private static List<TilePoint> ClipPolygonEdge(
        List<TilePoint> input,
        Func<TilePoint, bool> inside,
        Func<TilePoint, TilePoint, TilePoint> intersect)
    {
        if (input.Count == 0)
        {
            return [];
        }

        List<TilePoint> output = [];
        TilePoint previous = input[^1];
        bool previousInside = inside(previous);

        foreach (TilePoint current in input)
        {
            bool currentInside = inside(current);

            if (currentInside)
            {
                if (!previousInside)
                {
                    output.Add(intersect(previous, current));
                }

                output.Add(current);
            }
            else if (previousInside)
            {
                output.Add(intersect(previous, current));
            }

            previous = current;
            previousInside = currentInside;
        }

        return output;
    }

    private static TilePoint IntersectVertical(TilePoint a, TilePoint b, double x)
    {
        double dx = b.X - a.X;
        if (dx == 0)
        {
            return new TilePoint(x, a.Y);
        }

        double t = (x - a.X) / dx;
        return new TilePoint(x, a.Y + ((b.Y - a.Y) * t));
    }

    private static TilePoint IntersectHorizontal(TilePoint a, TilePoint b, double y)
    {
        double dy = b.Y - a.Y;
        if (dy == 0)
        {
            return new TilePoint(a.X, y);
        }

        double t = (y - a.Y) / dy;
        return new TilePoint(a.X + ((b.X - a.X) * t), y);
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
        public List<TileObjectData> Objects { get; set; } = [];
    }

    private sealed class TileObjectData
    {
        public required MapObject Object { get; init; }
        public required List<LatLong> Footprint { get; init; }
    }

    private readonly record struct Bounds(double MinLon, double MinLat, double MaxLon, double MaxLat);

    private readonly record struct TilePoint(double X, double Y);
}
