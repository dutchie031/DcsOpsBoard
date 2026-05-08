using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using DcsMissionParser.Net.Objects;
using DcsOpsBoard.Types;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using _2dTileExporter.Readers;
using DcsMissionParser.Net.CoordMapping;

namespace _2dTileExporter.Exporters;

public class DetailedTileExporter(Map map, int minZoomLevel, int maxZoomLevel, string sourceDirectory, string outputDirectory)
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

    public async Task ExportTiles()
    {
        Console.WriteLine("Building detailed source catalog...");
        List<SourceTile> sourceCatalog = BuildSourceCatalog();
        Console.WriteLine($"Found {sourceCatalog.Count} detailed source tiles.");

        Directory.CreateDirectory(outputDirectory);

        Console.WriteLine("Exporting detailed tiles...");

        var coverage = new Dictionary<string, List<string>>();
        for (int zoom = minZoomLevel; zoom <= maxZoomLevel; zoom++)
        {
            coverage[zoom.ToString(CultureInfo.InvariantCulture)] = await ExportZoomLevel(zoom, sourceCatalog);
            Console.WriteLine($"Completed detailed zoom level {zoom}. Exported {coverage[zoom.ToString(CultureInfo.InvariantCulture)].Count} tiles.");
        }

        string coveragePath = Path.Combine(outputDirectory, "coverage.json");
        var orderedCoverage = coverage
            .OrderBy(entry => int.Parse(entry.Key, CultureInfo.InvariantCulture))
            .ToDictionary(entry => entry.Key, entry => entry.Value);

        await File.WriteAllTextAsync(
            coveragePath,
            JsonSerializer.Serialize(orderedCoverage, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Detailed tile export complete. Coverage data written to {coveragePath}.");
    }

    private async Task<List<string>> ExportZoomLevel(int zoom, IReadOnlyList<SourceTile> sourceCatalog)
    {
        List<(int x, int y)> workItems = BuildCoverageTileList(zoom, sourceCatalog);

        totalTiles = workItems.Count;
        processed = 0;
        startTime = DateTime.Now;

        string zRoot = Path.Combine(outputDirectory, zoom.ToString(CultureInfo.InvariantCulture));
        if (Directory.Exists(zRoot))
        {
            Directory.Delete(zRoot, true);
        }

        Directory.CreateDirectory(zRoot);

        Console.WriteLine($"Generating detailed z{zoom}: {totalTiles} tiles...");
        if (totalTiles == 0)
        {
            return [];
        }

        int threads = Environment.ProcessorCount;
        int chunkSize = (workItems.Count + threads - 1) / threads;
        List<List<(int x, int y)>> threadGroups = [.. Enumerable.Range(0, threads)
            .Select(i => workItems.Skip(i * chunkSize).Take(chunkSize).ToList())
            .Where(group => group.Count > 0)];

        using var cts = new CancellationTokenSource();
        var createdTiles = new ConcurrentBag<(int x, int y)>();

        List<Task> tasks = [];
        foreach (List<(int x, int y)> group in threadGroups)
        {
            tasks.Add(Task.Run(async () =>
            {
                var localCache = new Dictionary<int, TerrainType[]>();
                foreach ((int xTile, int yTile) in group)
                {
                    try
                    {
                        if (await RenderDetailedTile(xTile, yTile, zoom, zRoot, sourceCatalog, localCache))
                        {
                            createdTiles.Add((xTile, yTile));
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARN] Failed to render detailed tile {xTile}/{yTile} z{zoom}: {ex.Message}");
                    }
                    finally
                    {
                        Interlocked.Increment(ref processed);
                    }
                }
            }));
        }

        Task workerCompletion = Task.WhenAll(tasks);
        Task logTask = Task.Run(() => LogProgress(zoom, cts.Token));

        await workerCompletion;
        await cts.CancelAsync();
        await logTask;

        Console.WriteLine($"Detailed z{zoom}: complete.");

        return [.. createdTiles
            .OrderBy(tile => tile.x)
            .ThenBy(tile => tile.y)
            .Select(tile => $"{tile.x}:{tile.y}")];
    }

    private async Task<bool> RenderDetailedTile(
        int xTile,
        int yTile,
        int zoom,
        string zRoot,
        IReadOnlyList<SourceTile> sourceCatalog,
        Dictionary<int, TerrainType[]> localCache)
    {
        TileLocalBounds tileBounds = GetTileLocalBounds(xTile, yTile, zoom);
        List<SourceTile> relevantSources = [.. sourceCatalog
            .Where(source => source.Intersects(tileBounds.MinX, tileBounds.MaxX, tileBounds.MinY, tileBounds.MaxY))
            .OrderBy(source => source.TileIndex)];

        if (relevantSources.Count == 0)
        {
            return false;
        }

        string xDir = Path.Combine(zRoot, xTile.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(xDir);

        string outputPath = Path.Combine(xDir, $"{yTile}.webp");
        double[] lonByPx = BuildLonLookup(xTile, zoom);
        double[] latByPy = BuildLatLookup(yTile, zoom);
        bool hasData = false;

        using var image = new Image<Rgba32>(OutputTileSize, OutputTileSize);
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

                    if (!TryGetTerrainType(local.X, local.Y, relevantSources, localCache, out TerrainType terrainType) ||
                        terrainType == TerrainType.UNKNOWN)
                    {
                        row[px] = new Rgba32(0, 0, 0, 0);
                        continue;
                    }

                    (int r, int g, int b) = terrainType.ToDefaultColor(map);
                    row[px] = new Rgba32((byte)r, (byte)g, (byte)b, 255);
                    hasData = true;
                }
            }
        });

        if (!hasData)
        {
            return false;
        }

        await image.SaveAsync(outputPath, Encoder);
        return true;
    }

    private bool TryGetTerrainType(
        double localX,
        double localY,
        IReadOnlyList<SourceTile> relevantSources,
        Dictionary<int, TerrainType[]> localCache,
        out TerrainType terrainType)
    {
        foreach (SourceTile sourceTile in relevantSources)
        {
            if (!sourceTile.Contains(localX, localY) || !TryGetSampleIndex(sourceTile, localX, localY, out int sampleIndex))
            {
                continue;
            }

            if (!localCache.TryGetValue(sourceTile.TileIndex, out TerrainType[]? terrainData))
            {
                terrainData = TerrainTypeReader.ReadAll(sourceTile.TerrainDataPath);
                localCache[sourceTile.TileIndex] = terrainData;
            }

            if (sampleIndex < 0 || sampleIndex >= terrainData.Length)
            {
                break;
            }

            terrainType = terrainData[sampleIndex];
            return true;
        }

        terrainType = TerrainType.UNKNOWN;
        return false;
    }

    private static bool TryGetSampleIndex(SourceTile tile, double localX, double localY, out int index)
    {
        int xSample = (int)Math.Round((localX - tile.MinX) / tile.SampleInterval);
        int ySample = (int)Math.Round((localY - tile.MinY) / tile.SampleInterval);

        if (xSample < 0 || xSample >= tile.SampleCountX || ySample < 0 || ySample >= tile.SampleCountY)
        {
            index = -1;
            return false;
        }

        index = (xSample * tile.SampleCountY) + ySample;
        return true;
    }

    private List<SourceTile> BuildSourceCatalog()
    {
        string[] metadataFiles = Directory.GetFiles(sourceDirectory, "metadata_*.json");
        Array.Sort(metadataFiles, StringComparer.OrdinalIgnoreCase);

        var sourceTiles = new List<SourceTile>(metadataFiles.Length);
        foreach (string metadataFile in metadataFiles)
        {
            int tileIndex = ParseTileIndex(metadataFile);
            DetailedTerrainMetadata metadata = TerrainMetadataReader.ReadDetailedFromFile(metadataFile);
            string terrainDataPath = Path.Combine(sourceDirectory, $"tile_{tileIndex}.td.bin");

            if (!File.Exists(terrainDataPath))
            {
                throw new FileNotFoundException($"Missing terrain data file for tile index {tileIndex}", terrainDataPath);
            }

            double minX = Math.Min(metadata.TopLeft.X, metadata.BottomRight.X);
            double maxX = Math.Max(metadata.TopLeft.X, metadata.BottomRight.X);
            double minY = Math.Min(metadata.TopLeft.Y, metadata.BottomRight.Y);
            double maxY = Math.Max(metadata.TopLeft.Y, metadata.BottomRight.Y);

            int sampleCountX = GetSampleCount(minX, maxX, metadata.SampleInterval, metadataFile);
            int sampleCountY = GetSampleCount(minY, maxY, metadata.SampleInterval, metadataFile);
            long expectedSamples = (long)sampleCountX * sampleCountY;
            long actualSamples = new FileInfo(terrainDataPath).Length;

            if (actualSamples != expectedSamples)
            {
                throw new InvalidOperationException(
                    $"Detailed terrain data size mismatch for tile {tileIndex}. Expected {expectedSamples} samples, found {actualSamples} bytes in {terrainDataPath}.");
            }

            sourceTiles.Add(new SourceTile
            {
                TileIndex = tileIndex,
                MinX = minX,
                MaxX = maxX,
                MinY = minY,
                MaxY = maxY,
                SampleInterval = metadata.SampleInterval,
                SampleCountX = sampleCountX,
                SampleCountY = sampleCountY,
                TerrainDataPath = terrainDataPath
            });
        }

        if (sourceTiles.Count == 0)
        {
            throw new InvalidOperationException($"No detailed source metadata files found in {sourceDirectory}");
        }

        return sourceTiles;
    }

    private List<(int x, int y)> BuildCoverageTileList(int zoom, IReadOnlyList<SourceTile> sourceCatalog)
    {
        var coverage = new HashSet<(int x, int y)>();
        foreach (SourceTile sourceTile in sourceCatalog)
        {
            foreach ((int x, int y) tile in GetCoverageTilesForSource(sourceTile, zoom))
            {
                coverage.Add(tile);
            }
        }

        return [.. coverage.OrderBy(tile => tile.x).ThenBy(tile => tile.y)];
    }

    private IEnumerable<(int x, int y)> GetCoverageTilesForSource(SourceTile sourceTile, int zoom)
    {
        var corners = new[]
        {
            map.CoordConverter.LOtoLL(new() { X = sourceTile.MinX, Y = sourceTile.MinY }),
            map.CoordConverter.LOtoLL(new() { X = sourceTile.MinX, Y = sourceTile.MaxY }),
            map.CoordConverter.LOtoLL(new() { X = sourceTile.MaxX, Y = sourceTile.MinY }),
            map.CoordConverter.LOtoLL(new() { X = sourceTile.MaxX, Y = sourceTile.MaxY })
        };

        double minLon = corners.Min(corner => corner.Lon);
        double maxLon = corners.Max(corner => corner.Lon);
        double minLat = corners.Min(corner => corner.Lat);
        double maxLat = corners.Max(corner => corner.Lat);

        int minX = LonToTileX(minLon, zoom);
        int maxX = LonToTileX(maxLon, zoom);
        int minY = LatToTileY(maxLat, zoom);
        int maxY = LatToTileY(minLat, zoom);

        for (int x = Math.Min(minX, maxX); x <= Math.Max(minX, maxX); x++)
        {
            for (int y = Math.Min(minY, maxY); y <= Math.Max(minY, maxY); y++)
            {
                yield return (x, y);
            }
        }
    }

    private async Task LogProgress(int zoom, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && Volatile.Read(ref processed) < totalTiles)
        {
            int done = Volatile.Read(ref processed);
            TimeSpan elapsed = DateTime.Now - startTime;
            string etaString = done == 0
                ? "estimating..."
                : TimeSpan.FromSeconds((elapsed.TotalSeconds / done) * (totalTiles - done)).ToString("hh\\:mm\\:ss");

            Console.WriteLine($"Progress: {done}/{totalTiles} detailed z{zoom} tiles... ETA: {etaString}");

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

    private TileLocalBounds GetTileLocalBounds(int xTile, int yTile, int zoom)
    {
        double westLon = TileXToLon(xTile, zoom);
        double eastLon = TileXToLon(xTile + 1, zoom);
        double northLat = TileYToLat(yTile, zoom);
        double southLat = TileYToLat(yTile + 1, zoom);

        var corners = new[]
        {
            map.CoordConverter.LLtoLO(new LatLong { Lat = northLat, Lon = westLon }),
            map.CoordConverter.LLtoLO(new LatLong { Lat = northLat, Lon = eastLon }),
            map.CoordConverter.LLtoLO(new LatLong { Lat = southLat, Lon = westLon }),
            map.CoordConverter.LLtoLO(new LatLong { Lat = southLat, Lon = eastLon })
        };

        return new TileLocalBounds
        {
            MinX = corners.Min(corner => corner.X),
            MaxX = corners.Max(corner => corner.X),
            MinY = corners.Min(corner => corner.Y),
            MaxY = corners.Max(corner => corner.Y)
        };
    }

    private static int GetSampleCount(double minValue, double maxValue, int sampleInterval, string metadataFile)
    {
        if (sampleInterval <= 0)
        {
            throw new InvalidOperationException($"Invalid sample interval in {metadataFile}: {sampleInterval}");
        }

        double span = maxValue - minValue;
        // Match Lua numeric for-loop semantics: for v=start,end,step do ...
        // Number of iterations is floor((end-start)/step) + 1 when step > 0.
        double steps = (span / sampleInterval) + 1e-9;
        int sampleCount = (int)Math.Floor(steps) + 1;
        if (sampleCount <= 0)
        {
            throw new InvalidOperationException($"Invalid sample count in {metadataFile}");
        }

        return sampleCount;
    }

    private static int ParseTileIndex(string metadataFile)
    {
        string fileName = Path.GetFileNameWithoutExtension(metadataFile);
        const string Prefix = "metadata_";

        if (!fileName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(fileName[Prefix.Length..], out int tileIndex))
        {
            throw new InvalidOperationException($"Unable to parse detailed tile index from {metadataFile}");
        }

        return tileIndex;
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

    private static double TileXToLon(double xTile, int zoom)
    {
        double n = 1 << zoom;
        return (xTile / n * 360.0) - 180.0;
    }

    private static double TileYToLat(double yTile, int zoom)
    {
        double n = 1 << zoom;
        double mercY = Math.PI * (1.0 - (2.0 * yTile / n));
        return Math.Atan(Math.Sinh(mercY)) * 180.0 / Math.PI;
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

    private sealed class SourceTile
    {
        public required int TileIndex { get; init; }
        public required double MinX { get; init; }
        public required double MaxX { get; init; }
        public required double MinY { get; init; }
        public required double MaxY { get; init; }
        public required int SampleInterval { get; init; }
        public required int SampleCountX { get; init; }
        public required int SampleCountY { get; init; }
        public required string TerrainDataPath { get; init; }

        public bool Contains(double localX, double localY)
        {
            return localX >= MinX && localX <= MaxX && localY >= MinY && localY <= MaxY;
        }

        public bool Intersects(double minX, double maxX, double minY, double maxY)
        {
            return !(MaxX < minX || MinX > maxX || MaxY < minY || MinY > maxY);
        }
    }

    private sealed class TileLocalBounds
    {
        public required double MinX { get; init; }
        public required double MaxX { get; init; }
        public required double MinY { get; init; }
        public required double MaxY { get; init; }
    }

}
