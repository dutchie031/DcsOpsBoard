using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace _2dTileExporter.Exporters;

public class ZoomLayerExporter(int baseZoom, int minZoom, string tilesRoot)
{
    private const int TileSize = 256;
    private static readonly WebpEncoder Encoder = new()
    {
        FileFormat = WebpFileFormatType.Lossy,
        Quality = 82,
        Method = WebpEncodingMethod.BestQuality
    };

    private int processed;
    private int totalTiles;
    private DateTime startTime;

    public async Task Export()
    {
        for (int zoom = baseZoom - 1; zoom >= minZoom; zoom--)
        {
            int sourceZoom = zoom + 1;
            string sourceZoomDir = Path.Combine(tilesRoot, sourceZoom.ToString());

            if (!Directory.Exists(sourceZoomDir))
            {
                Console.WriteLine($"Skipping z{zoom}: source directory z{sourceZoom} not found.");
                continue;
            }

            // Collect unique parent (x, y) coordinates from source tiles
            var parents = new HashSet<(int x, int y)>();
            foreach (string xDir in Directory.EnumerateDirectories(sourceZoomDir))
            {
                if (!int.TryParse(Path.GetFileName(xDir), out int childX)) continue;
                foreach (string yFile in Directory.EnumerateFiles(xDir, "*.webp"))
                {
                    if (!int.TryParse(Path.GetFileNameWithoutExtension(yFile), out int childY)) continue;
                    parents.Add((childX / 2, childY / 2));
                }
            }

            string outputZoomDir = Path.Combine(tilesRoot, zoom.ToString());
            Directory.CreateDirectory(outputZoomDir);

            totalTiles = parents.Count;
            processed = 0;
            startTime = DateTime.Now;

            Console.WriteLine($"z{zoom}: generating {totalTiles} tiles from z{sourceZoom}...");

            int threads = Environment.ProcessorCount;
            var workItems = parents.ToList();
            int chunkSize = (workItems.Count + threads - 1) / threads;

            List<List<(int x, int y)>> threadGroups = [.. Enumerable.Range(0, threads)
                .Select(i => workItems.Skip(i * chunkSize).Take(chunkSize).ToList())];

            List<Task> tasks = [];
            foreach (var group in threadGroups)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        foreach (var (x, y) in group)
                        {
                            await RenderParentTile(x, y, sourceZoom, sourceZoomDir, outputZoomDir);
                            Interlocked.Increment(ref processed);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error rendering tile: {ex.Message}");
                    }
                }));
            }

            tasks.Add(Task.Run(LogProgress));
            await Task.WhenAll(tasks);

            Console.WriteLine($"z{zoom}: complete ({totalTiles} tiles).");
        }

        Console.WriteLine("Pyramid export complete.");
    }

    private async Task LogProgress()
    {
        while (Volatile.Read(ref processed) < totalTiles)
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

            Console.WriteLine($"  Progress: {done}/{totalTiles} tiles... ETA: {etaString}");
            await Task.Delay(5000);
        }
    }

    private static async Task RenderParentTile(int x, int y, int sourceZoom, string sourceZoomDir, string outputZoomDir)
    {
        // 4 children: (2x, 2y)=top-left, (2x+1, 2y)=top-right, (2x, 2y+1)=bottom-left, (2x+1, 2y+1)=bottom-right
        (int cx, int cy, int ox, int oy)[] children =
        [
            (2 * x,     2 * y,     0,        0),
            (2 * x + 1, 2 * y,     TileSize, 0),
            (2 * x,     2 * y + 1, 0,        TileSize),
            (2 * x + 1, 2 * y + 1, TileSize, TileSize),
        ];

        using var canvas = new Image<Rgb24>(TileSize * 2, TileSize * 2);

        foreach (var (cx, cy, ox, oy) in children)
        {
            string childPath = Path.Combine(sourceZoomDir, cx.ToString(), $"{cy}.webp");
            if (File.Exists(childPath))
            {
                using var child = await Image.LoadAsync<Rgb24>(childPath);
                canvas.Mutate(ctx => ctx.DrawImage(child, new Point(ox, oy), 1f));
            }
            // Missing children remain black (default Rgb24 background)
        }

        // Downsample 512→256 with Lanczos3 for quality
        canvas.Mutate(ctx => ctx.Resize(TileSize, TileSize, KnownResamplers.Lanczos3));

        string xDir = Path.Combine(outputZoomDir, x.ToString());
        Directory.CreateDirectory(xDir);
        string outputPath = Path.Combine(xDir, $"{y}.webp");
        await canvas.SaveAsync(outputPath, Encoder);
    }
}

