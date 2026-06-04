using System.Collections.Concurrent;
using System.Text.Json;
using DcsMissionParser.Net.Objects;
using DcsOpsBoard.Types;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using _2dTileExporter.Readers;
using DcsMissionParser.Net.CoordMapping;
using System.Text.Json.Serialization;

namespace _2dTileExporter.Exporters;

public class BaseLayerExporter(Map map, int targetZoom,string sourceDirectory, string outputDirectory, bool darkMode = false)
{
    private const int OutputTileSize = 256;
    private readonly int TargetZoom = targetZoom;
    private const int WebpQuality = 82;

    private int processed;
    private int totalTiles;
    private DateTime startTime = DateTime.Now;

    public async Task ExportBaseSamples()
    {
        processed = 0;
        startTime = DateTime.Now;


        Console.WriteLine("Building source catalog...");
        var sourceCatalog = BuildSourceCatalog();
        Console.WriteLine($"Found {sourceCatalog.Count} source tiles.");
        var global = ReadGlobalMetadata();

        Console.WriteLine("Building source lookup...");

        int tileSpanMeters = global.TileSize * global.SampleInterval;
        var sourceLookup = BuildSourceLookup(sourceCatalog, global.TopLeft.X, global.TopLeft.Y, tileSpanMeters);

        Console.WriteLine("Building target tile list...");

        var (minX, maxX, minY, maxY) = GetCoverageTileBounds();
        var workItems = BuildTargetTileList(minX, maxX, minY, maxY);

        Console.WriteLine($"Target tile bounds: X [{minX}, {maxX}], Y [{minY}, {maxY}]");
        Console.WriteLine($"Total target tiles to process: {workItems.Count}");
        Console.WriteLine("Starting export...");

        totalTiles = workItems.Count;

        string zRoot = Path.Combine(outputDirectory, TargetZoom.ToString());
        if (Directory.Exists(zRoot))
        {
            Console.WriteLine($"Output directory {zRoot} already exists. Deleting...");
            Directory.Delete(zRoot, true);
        }
        Directory.CreateDirectory(zRoot);

        int threads = Environment.ProcessorCount;
        Console.WriteLine($"Using {threads} threads for export...");
        Console.WriteLine($"Exporting {totalTiles} z{TargetZoom} tiles...");

        int chunkSize = (workItems.Count + threads - 1) / threads;
        List<List<(int x, int y)>> threadGroups = [.. Enumerable.Range(0, threads)
            .Select(i => workItems.Skip(i * chunkSize).Take(chunkSize).ToList())
            .Where(group => group.Count > 0)];

        using var cts = new CancellationTokenSource();
        List<Task> exportTasks = [];
        foreach (var group in threadGroups)
        {
            exportTasks.Add(Task.Run(() => ProcessTargetGroup(group, sourceLookup, global.TopLeft.X, global.TopLeft.Y, tileSpanMeters, zRoot)));
        }

        Task workerCompletion = Task.WhenAll(exportTasks);
        Task logTask = Task.Run(() => LogProgress(cts.Token));

        await workerCompletion;
        await cts.CancelAsync();
        await logTask;

        Console.WriteLine("Export complete.");
    }

    private async Task LogProgress(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && Volatile.Read(ref processed) < totalTiles)
        {
            int done = Volatile.Read(ref processed);
            TimeSpan elapsed = DateTime.Now - startTime;

            string etaString;
            if (done == 0)
            {
                etaString = "estimating...";
            }
            else
            {
                double secPerTile = elapsed.TotalSeconds / done;
                double secLeft = secPerTile * (totalTiles - done);
                etaString = TimeSpan.FromSeconds(secLeft).ToString("hh\\:mm\\:ss");
            }

            Console.WriteLine($"Progress: {done}/{totalTiles} tiles processed... ETA: {etaString}");
            try
            {
                await Task.Delay(5000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Console.WriteLine($"Progress: {totalTiles}/{totalTiles} tiles processed... ETA: 00:00:00");
    }

    private async Task ProcessTargetGroup(
        List<(int x, int y)> workItems,
        Dictionary<long, SourceTile> sourceLookup,
        int topLeftX,
        int topLeftY,
        int tileSpanMeters,
        string zRoot)
    {
        var localCache = new Dictionary<int, TerrainType[]>();
        int currentX = int.MinValue;

        foreach (var (xTile, yTile) in workItems)
        {
            try
            {
                // When moving to a new x column, source tiles from the previous column
                // are no longer needed — clear cache to bound memory usage.
                if (xTile != currentX)
                {
                    localCache.Clear();
                    currentX = xTile;
                }

                string xDir = Path.Combine(zRoot, xTile.ToString());
                Directory.CreateDirectory(xDir);

                using var image = new Image<Rgba32>(OutputTileSize, OutputTileSize);

                double[] lonByPx = BuildLonLookup(xTile);
                double[] latByPy = BuildLatLookup(yTile);

                image.ProcessPixelRows(accessor =>
                {
                    for (int py = 0; py < OutputTileSize; py++)
                    {
                        var row = accessor.GetRowSpan(py);
                        double lat = latByPy[py];

                        for (int px = 0; px < OutputTileSize; px++)
                        {
                            double lon = lonByPx[px];
                            var local = map.CoordConverter.LLtoLO(new LatLong { Lat = lat, Lon = lon });

                            if (!TryResolveSourceTile(sourceLookup, topLeftX, topLeftY, tileSpanMeters, local.X, local.Y, out SourceTile? sourceTile) || sourceTile is null)
                            {
                                row[px] = new Rgba32(0, 0, 0, 0);
                                continue;
                            }

                            if (!TryGetSampleIndex(sourceTile, local.X, local.Y, out int idx))
                            {
                                row[px] = new Rgba32(0, 0, 0, 0);
                                continue;
                            }

                            if (!localCache.TryGetValue(sourceTile.TileIndex, out var terrain))
                            {
                                terrain = TerrainTypeReader.ReadAll(sourceTile.TerrainDataPath);
                                localCache[sourceTile.TileIndex] = terrain;
                            }

                            var (r, g, b) = darkMode ? terrain[idx].ToDarkColor(map) : terrain[idx].ToDefaultColor(map);
                            row[px] = new Rgba32((byte)r, (byte)g, (byte)b, 255);
                        }
                    }
                });

                string outputPath = Path.Combine(xDir, $"{yTile}.webp");
                await image.SaveAsync(outputPath, new WebpEncoder
                {
                    FileFormat = WebpFileFormatType.Lossy,
                    Quality = WebpQuality,
                    Method = WebpEncodingMethod.BestQuality
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Failed to render base tile {xTile}/{yTile} z{TargetZoom}: {ex.Message}");
            }
            finally
            {
                Interlocked.Increment(ref processed);
            }
        }
    }

    private (int minX, int maxX, int minY, int maxY) GetCoverageTileBounds()
    {
        var limits = map.Limits;

        int minX = LonToTileX(limits.BottomLeft.Lon, TargetZoom);
        int maxX = LonToTileX(limits.TopRight.Lon, TargetZoom);
        int minY = LatToTileY(limits.TopRight.Lat, TargetZoom);
        int maxY = LatToTileY(limits.BottomLeft.Lat, TargetZoom);

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

    private double[] BuildLonLookup(int xTile)
    {
        double[] lookup = new double[OutputTileSize];
        double n = 1 << TargetZoom;

        for (int px = 0; px < OutputTileSize; px++)
        {
            double fx = xTile + (px + 0.5) / OutputTileSize;
            lookup[px] = fx / n * 360.0 - 180.0;
        }

        return lookup;
    }

    private double[] BuildLatLookup(int yTile)
    {
        double[] lookup = new double[OutputTileSize];
        double n = 1 << TargetZoom;

        for (int py = 0; py < OutputTileSize; py++)
        {
            double fy = yTile + (py + 0.5) / OutputTileSize;
            double mercY = Math.PI * (1.0 - 2.0 * fy / n);
            lookup[py] = Math.Atan(Math.Sinh(mercY)) * 180.0 / Math.PI;
        }

        return lookup;
    }

    private static bool TryResolveSourceTile(
        Dictionary<long, SourceTile> sourceLookup,
        int topLeftX,
        int topLeftY,
        int tileSpanMeters,
        double localX,
        double localY,
        out SourceTile? tile)
    {
        int row = (int)Math.Floor((topLeftX - localX) / tileSpanMeters);
        int col = (int)Math.Floor((localY - topLeftY) / tileSpanMeters);

        long key = BuildKey(row, col);
        return sourceLookup.TryGetValue(key, out tile);
    }

    private static bool TryGetSampleIndex(SourceTile tile, double localX, double localY, out int index)
    {
        int row = (int)Math.Round((tile.OriginX - localX) / tile.SampleInterval);
        int col = (int)Math.Round((localY - tile.OriginY) / tile.SampleInterval);

        if (row < 0 || row >= tile.TileSamples || col < 0 || col >= tile.TileSamples)
        {
            index = -1;
            return false;
        }

        index = row * tile.TileSamples + col;
        return true;
    }

    private Dictionary<int, SourceTile> BuildSourceCatalog()
    {
        var metadataFiles = Directory.GetFiles(sourceDirectory, "tile_*_metadata.json");
        var catalog = new Dictionary<int, SourceTile>(metadataFiles.Length);

        foreach (var metadataFile in metadataFiles)
        {
            TerrainMetadata metadata = TerrainMetadataReader.ReadFromFile(metadataFile);
            string tdPath = Path.Combine(sourceDirectory, $"tile_{metadata.TileIndex}.td.bin");

            if (!File.Exists(tdPath))
            {
                throw new FileNotFoundException($"Missing terrain data file for tile index {metadata.TileIndex}", tdPath);
            }

            catalog[metadata.TileIndex] = new SourceTile
            {
                TileIndex = metadata.TileIndex,
                OriginX = metadata.TileOrigin.X,
                OriginY = metadata.TileOrigin.Y,
                TileSize = metadata.TileSize,
                TileSamples = metadata.TileSamples,
                SampleInterval = metadata.SampleInterval,
                TerrainDataPath = tdPath
            };
        }

        if (catalog.Count == 0)
        {
            throw new InvalidOperationException($"No source tile metadata files found in {sourceDirectory}");
        }

        return catalog;
    }

    private static Dictionary<long, SourceTile> BuildSourceLookup(
        Dictionary<int, SourceTile> sourceByIndex,
        int topLeftX,
        int topLeftY,
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

    private GlobalMetadata ReadGlobalMetadata()
    {
        string metadataPath = Path.Combine(sourceDirectory, "metadata.json");
        if (!File.Exists(metadataPath))
        {
            throw new FileNotFoundException("Global metadata.json was not found", metadataPath);
        }

        string json = File.ReadAllText(metadataPath);
        var metadata = JsonSerializer.Deserialize<GlobalMetadata>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize {metadataPath}");

        return metadata;
    }

    private static long BuildKey(int row, int col) => ((long)row << 32) | (uint)col;

    private sealed class SourceTile
    {
        public required int TileIndex { get; init; }
        public required double OriginX { get; init; }
        public required double OriginY { get; init; }
        public required int TileSize { get; init; }
        public required int TileSamples { get; init; }
        public required int SampleInterval { get; init; }
        public required string TerrainDataPath { get; init; }
    }

    private sealed class GlobalMetadata
    {
        [JsonPropertyName("topLeft")]
        public required Point TopLeft { get; init; }

        [JsonPropertyName("bottomRight")]
        public required Point BottomRight { get; init; }

        [JsonPropertyName("sampleInterval")]
        public required int SampleInterval { get; init; }

        [JsonPropertyName("tileSize")]
        public required int TileSize { get; init; }
    }

    private sealed class Point
    {
        [JsonPropertyName("x")]
        public int X { get; init; }

        [JsonPropertyName("y")]
        public int Y { get; init; }
    }
}

