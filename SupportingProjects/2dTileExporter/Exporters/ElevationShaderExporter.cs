using System.Collections.Concurrent;
using System.Text.Json;
using DcsMissionParser.Net.CoordMapping;
using DcsMissionParser.Net.Objects;
using DcsOpsBoard.Types;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using _2dTileExporter.Readers;

namespace _2dTileExporter.Exporters;

public class ElevationShaderExporter(Map map, int minLevel, int maxLevel, string sourceDir, string outputDir)
{
    private const int OutputTileSize = 256;
    private static readonly WebpEncoder Encoder = new()
    {
        FileFormat = WebpFileFormatType.Lossless,
        Method = WebpEncodingMethod.BestQuality
    };

    private int processed;
    private int totalTiles;
    private DateTime startTime;

    public async Task Export()
    {
        var sourceCatalog = BuildSourceCatalog();
        var global = ReadGlobalMetadata();
        int tileSpanMeters = global.TileSize * global.SampleInterval;
        var sourceLookup = BuildSourceLookup(sourceCatalog, global.TopLeft.X, global.TopLeft.Y, tileSpanMeters);

        for (int zoom = maxLevel; zoom >= minLevel; zoom--)
        {
            await ExportZoomLevel(zoom, sourceCatalog, sourceLookup, global, tileSpanMeters);
        }
    }

    private async Task ExportZoomLevel(
        int zoom,
        Dictionary<int, SourceTile> sourceCatalog,
        Dictionary<long, SourceTile> sourceLookup,
        GlobalTerrainMetadata global,
        int tileSpanMeters)
    {
        var (minX, maxX, minY, maxY) = GetCoverageTileBounds(zoom);
        var workItems = BuildTargetTileList(minX, maxX, minY, maxY);

        totalTiles = workItems.Count;
        processed = 0;
        startTime = DateTime.Now;

        string zRoot = Path.Combine(outputDir, zoom.ToString());
        Directory.CreateDirectory(zRoot);

        Console.WriteLine($"Generating hillshade z{zoom}: {totalTiles} tiles...");

        int threads = Environment.ProcessorCount;
        if (zoom <= 8)
        {
            threads = 1; // Avoid excessive threading on low zoom levels with few tiles
        }

        int chunkSize = (workItems.Count + threads - 1) / threads;
        List<List<(int x, int y)>> threadGroups = [.. Enumerable.Range(0, threads)
            .Select(i => workItems.Skip(i * chunkSize).Take(chunkSize).ToList())];

        using var cts = new CancellationTokenSource();

        List<Task> tasks = [];
        foreach (var group in threadGroups)
        {
            tasks.Add(Task.Run(async () =>
            {
                var localCache = new Dictionary<int, int[]>();
                foreach (var (xTile, yTile) in group)
                {
                    try
                    {
                        await RenderHillshadeTile(xTile, yTile, zoom, zRoot, sourceLookup, global, tileSpanMeters, localCache);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARN] Failed to render hillshade tile {xTile}/{yTile} z{zoom}: {ex.Message}");
                    }
                    finally
                    {
                        Interlocked.Increment(ref processed);
                    }
                }
            }));
        }

        Task workerCompletion = Task.WhenAll(tasks);
        Task logTask = Task.Run(() => LogProgress(cts.Token));
        await workerCompletion;
        await cts.CancelAsync();
        await logTask;
        Console.WriteLine($"Hillshade z{zoom}: complete.");
    }

    private async Task RenderHillshadeTile(
        int xTile,
        int yTile,
        int zoom,
        string zRoot,
        Dictionary<long, SourceTile> sourceLookup,
        GlobalTerrainMetadata global,
        int tileSpanMeters,
        Dictionary<int, int[]> localCache)
    {
        string xDir = Path.Combine(zRoot, xTile.ToString());
        Directory.CreateDirectory(xDir);

        string outputPath = Path.Combine(xDir, $"{yTile}.webp");
        if(File.Exists(outputPath))
        {
            return;
        }

        using var image = new Image<Rgba32>(OutputTileSize, OutputTileSize);
        double[] lonByPx = BuildLonLookup(xTile, zoom);
        double[] latByPy = BuildLatLookup(yTile, zoom);

        image.ProcessPixelRows(accessor =>
        {
            for (int py = 0; py < OutputTileSize; py++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(py);
                double lat = latByPy[py];

                for (int px = 0; px < OutputTileSize; px++)
                {
                    double lon = lonByPx[px];
                    var local = map.CoordConverter.LLtoLO(new LatLong { Lat = lat, Lon = lon });

                    if (!TryComputeHillshade(local.X, local.Y, sourceLookup, global, tileSpanMeters, localCache, out byte shade))
                    {
                        row[px] = new Rgba32(0, 0, 0, 0);
                        continue;
                    }

                    row[px] = new Rgba32(shade, shade, shade, 255);
                }
            }
        });

        await image.SaveAsync(outputPath, Encoder);
    }

    private static bool TryComputeHillshade(
        double localX,
        double localY,
        Dictionary<long, SourceTile> sourceLookup,
        GlobalTerrainMetadata global,
        int tileSpanMeters,
        Dictionary<int, int[]> localCache,
        out byte shade)
    {
        double spacing = global.HeightMapInterval;

        if (!TrySampleHeight(localX - spacing, localY + spacing, sourceLookup, global, tileSpanMeters, localCache, out double z1) ||
            !TrySampleHeight(localX, localY + spacing, sourceLookup, global, tileSpanMeters, localCache, out double z2) ||
            !TrySampleHeight(localX + spacing, localY + spacing, sourceLookup, global, tileSpanMeters, localCache, out double z3) ||
            !TrySampleHeight(localX - spacing, localY, sourceLookup, global, tileSpanMeters, localCache, out double z4) ||
            !TrySampleHeight(localX + spacing, localY, sourceLookup, global, tileSpanMeters, localCache, out double z6) ||
            !TrySampleHeight(localX - spacing, localY - spacing, sourceLookup, global, tileSpanMeters, localCache, out double z7) ||
            !TrySampleHeight(localX, localY - spacing, sourceLookup, global, tileSpanMeters, localCache, out double z8) ||
            !TrySampleHeight(localX + spacing, localY - spacing, sourceLookup, global, tileSpanMeters, localCache, out double z9))
        {
            shade = 0;
            return false;
        }

        double dzdx = ((z3 + (2 * z6) + z9) - (z1 + (2 * z4) + z7)) / (8 * spacing);
        double dzdy = ((z7 + (2 * z8) + z9) - (z1 + (2 * z2) + z3)) / (8 * spacing);

        const double sunAzimuthDegrees = 315;
        const double sunElevationDegrees = 45;

        double azimuth = DegreesToRadians(360 - sunAzimuthDegrees + 90);
        double zenith = DegreesToRadians(90 - sunElevationDegrees);
        double slope = Math.Atan(Math.Sqrt((dzdx * dzdx) + (dzdy * dzdy)));
        double aspect = Math.Atan2(dzdy, -dzdx);
        if (aspect < 0)
        {
            aspect += Math.PI * 2;
        }

        double intensity =
            (Math.Cos(zenith) * Math.Cos(slope)) +
            (Math.Sin(zenith) * Math.Sin(slope) * Math.Cos(azimuth - aspect));

        shade = (byte)Math.Clamp((int)Math.Round(Math.Max(0, intensity) * 255), 0, 255);
        return true;
    }

    private static bool TrySampleHeight(
        double localX,
        double localY,
        Dictionary<long, SourceTile> sourceLookup,
        GlobalTerrainMetadata global,
        int tileSpanMeters,
        Dictionary<int, int[]> localCache,
        out double height)
    {
        if (!TryResolveSourceTile(sourceLookup, global.TopLeft.X, global.TopLeft.Y, tileSpanMeters, localX, localY, out SourceTile? sourceTile) || sourceTile is null)
        {
            height = 0;
            return false;
        }

        if (!TryGetHeightSampleIndex(sourceTile, global, localX, localY, out int sampleIndex))
        {
            height = 0;
            return false;
        }

        if (!localCache.TryGetValue(sourceTile.TileIndex, out int[]? heights))
        {
            heights = HeightMapReader.ReadAll(sourceTile.HeightDataPath);
            localCache[sourceTile.TileIndex] = heights;
        }

        if (sampleIndex < 0 || sampleIndex >= heights.Length)
        {
            height = 0;
            return false;
        }

        height = heights[sampleIndex];
        return true;
    }

    private async Task LogProgress(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && Volatile.Read(ref processed) < totalTiles)
        {
            int done = Volatile.Read(ref processed);
            TimeSpan elapsed = DateTime.Now - startTime;
            string etaString = done == 0
                ? "estimating..."
                : TimeSpan.FromSeconds((elapsed.TotalSeconds / done) * (totalTiles - done)).ToString("hh\\:mm\\:ss");

            Console.WriteLine($"Progress: {done}/{totalTiles} hillshade tiles... ETA: {etaString}");
            try
            {
                await Task.Delay(5000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private (int minX, int maxX, int minY, int maxY) GetCoverageTileBounds(int zoom)
    {
        var limits = map.Limits;

        int minX = LonToTileX(limits.BottomLeft.Lon, zoom);
        int maxX = LonToTileX(limits.TopRight.Lon, zoom);
        int minY = LatToTileY(limits.TopRight.Lat, zoom);
        int maxY = LatToTileY(limits.BottomLeft.Lat, zoom);

        return (
            Math.Min(minX, maxX),
            Math.Max(minX, maxX),
            Math.Min(minY, maxY),
            Math.Max(minY, maxY));
    }

    private static List<(int x, int y)> BuildTargetTileList(int minX, int maxX, int minY, int maxY)
    {
        int count = (maxX - minX + 1) * (maxY - minY + 1);
        var list = new List<(int x, int y)>(count);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                list.Add((x, y));
            }
        }

        return list;
    }

    private static int LonToTileX(double lonDeg, int zoom)
    {
        double n = 1 << zoom;
        return (int)Math.Floor((lonDeg + 180.0) / 360.0 * n);
    }

    private static int LatToTileY(double latDeg, int zoom)
    {
        double latRad = latDeg * Math.PI / 180.0;
        double n = 1 << zoom;
        double y = (1.0 - Math.Log(Math.Tan(latRad) + (1.0 / Math.Cos(latRad))) / Math.PI) / 2.0 * n;
        return (int)Math.Floor(y);
    }

    private static double[] BuildLonLookup(int xTile, int zoom)
    {
        double[] lookup = new double[OutputTileSize];
        double n = 1 << zoom;

        for (int px = 0; px < OutputTileSize; px++)
        {
            double fx = xTile + (px + 0.5) / OutputTileSize;
            lookup[px] = (fx / n * 360.0) - 180.0;
        }

        return lookup;
    }

    private static double[] BuildLatLookup(int yTile, int zoom)
    {
        double[] lookup = new double[OutputTileSize];
        double n = 1 << zoom;

        for (int py = 0; py < OutputTileSize; py++)
        {
            double fy = yTile + (py + 0.5) / OutputTileSize;
            double mercY = Math.PI * (1.0 - (2.0 * fy / n));
            lookup[py] = Math.Atan(Math.Sinh(mercY)) * 180.0 / Math.PI;
        }

        return lookup;
    }

    private static bool TryResolveSourceTile(
        Dictionary<long, SourceTile> sourceLookup,
        double topLeftX,
        double topLeftY,
        int tileSpanMeters,
        double localX,
        double localY,
        out SourceTile? tile)
    {
        int row = (int)Math.Floor((topLeftX - localX) / tileSpanMeters);
        int col = (int)Math.Floor((localY - topLeftY) / tileSpanMeters);
        return sourceLookup.TryGetValue(BuildKey(row, col), out tile);
    }

    private static bool TryGetHeightSampleIndex(SourceTile tile, GlobalTerrainMetadata global, double localX, double localY, out int index)
    {
        int row = (int)Math.Round((tile.OriginX - localX) / global.HeightMapInterval);
        int col = (int)Math.Round((localY - tile.OriginY) / global.HeightMapInterval);

        if (row < 0 || row >= global.HeightMapSamples || col < 0 || col >= global.HeightMapSamples)
        {
            index = -1;
            return false;
        }

        index = (row * global.HeightMapSamples) + col;
        return true;
    }

    private Dictionary<int, SourceTile> BuildSourceCatalog()
    {
        string[] metadataFiles = Directory.GetFiles(sourceDir, "tile_*_metadata.json");
        var catalog = new Dictionary<int, SourceTile>(metadataFiles.Length);

        foreach (string metadataFile in metadataFiles)
        {
            TerrainMetadata metadata = TerrainMetadataReader.ReadFromFile(metadataFile);
            string hmPath = Path.Combine(sourceDir, $"tile_{metadata.TileIndex}.hm.bin");

            if (!File.Exists(hmPath))
            {
                throw new FileNotFoundException($"Missing height data file for tile index {metadata.TileIndex}", hmPath);
            }

            catalog[metadata.TileIndex] = new SourceTile
            {
                TileIndex = metadata.TileIndex,
                OriginX = metadata.TileOrigin.X,
                OriginY = metadata.TileOrigin.Y,
                TileSize = metadata.TileSize,
                TileSamples = metadata.TileSamples,
                SampleInterval = metadata.SampleInterval,
                HeightDataPath = hmPath
            };
        }

        if (catalog.Count == 0)
        {
            throw new InvalidOperationException($"No source tile metadata files found in {sourceDir}");
        }

        return catalog;
    }

    private static Dictionary<long, SourceTile> BuildSourceLookup(
        Dictionary<int, SourceTile> sourceByIndex,
        double topLeftX,
        double topLeftY,
        int tileSpanMeters)
    {
        var lookup = new Dictionary<long, SourceTile>(sourceByIndex.Count);

        foreach (var tile in sourceByIndex.Values)
        {
            int row = (int)Math.Floor((topLeftX - tile.OriginX) / (double)tileSpanMeters);
            int col = (int)Math.Floor((tile.OriginY - topLeftY) / (double)tileSpanMeters);
            lookup[BuildKey(row, col)] = tile;
        }

        return lookup;
    }

    private GlobalTerrainMetadata ReadGlobalMetadata()
    {
        string metadataPath = Path.Combine(sourceDir, "metadata.json");
        if (!File.Exists(metadataPath))
        {
            throw new FileNotFoundException("Global metadata.json was not found", metadataPath);
        }

        return TerrainMetadataReader.ReadGlobalFromFile(metadataPath);
    }

    private static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180.0);

    private static long BuildKey(int row, int col) => ((long)row << 32) | (uint)col;

    private sealed class SourceTile
    {
        public required int TileIndex { get; init; }
        public required double OriginX { get; init; }
        public required double OriginY { get; init; }
        public required int TileSize { get; init; }
        public required int TileSamples { get; init; }
        public required int SampleInterval { get; init; }
        public required string HeightDataPath { get; init; }
    }

}
