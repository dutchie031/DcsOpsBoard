using _2dTileExporter.Exporters;
using DcsOpsBoard.Types;


Dictionary<Map, MapConfig> mapConfigs = new()
{
    { 
        Map.Caucasus, 
        new MapConfig 
        { 
            SourceDir = @"C:\DCS_Exports\DcsOpsBoard\Caucasus", 
            OutputDir = @"C:\DCS_Exports\DcsOpsBoard\Caucasus_Tiles", 
            ShaderDir = @"C:\DCS_Exports\DcsOpsBoard\Caucasus_Shaders",
            DetailedSourceDir = @"C:\DCS_Exports\DcsOpsBoard\Detailed_Export\Caucasus",
            DetailedOutputDir = @"C:\DCS_Exports\DcsOpsBoard\Caucasus_Detailed_Tiles"
        } 
    },
    { 
        Map.Kola, 
        new MapConfig 
        { 
            SourceDir = @"C:\DCS_Exports\DcsOpsBoard\Kola", 
            OutputDir = @"C:\DCS_Exports\DcsOpsBoard\Kola_Tiles", 
            ShaderDir = @"C:\DCS_Exports\DcsOpsBoard\Kola_Shaders",
            DetailedSourceDir = @"C:\DCS_Exports\DcsOpsBoard\Detailed_Export\Kola",
            DetailedOutputDir = @"C:\DCS_Exports\DcsOpsBoard\Kola_Detailed_Tiles"
        } 
    },
};

Console.WriteLine("Select map to export:");
foreach (var kvp in mapConfigs)
{
    Console.WriteLine($"{(int)kvp.Key}: {kvp.Key}");
}

string input = Console.ReadLine() ?? string.Empty;
if (!int.TryParse(input, out int mapChoice) || !mapConfigs.ContainsKey((Map)mapChoice))
{
    Console.WriteLine("Invalid choice. Exiting.");
    return;
}
    
var map = (Map)mapChoice;
var config = mapConfigs[map];
var sourceDir = config.SourceDir;
var outputDir = config.OutputDir;
var shaderDir = config.ShaderDir;
var detailedSourceDir = config.DetailedSourceDir;
var detailedOutputDir = config.DetailedOutputDir;

Console.WriteLine($"Source: {sourceDir}");
Console.WriteLine($"Output: {outputDir}");
Console.WriteLine($"Shaders: {shaderDir}");
Console.WriteLine();
Console.WriteLine($"Detailed Source: {detailedSourceDir}");
Console.WriteLine($"Detailed Output: {detailedOutputDir}");

int action = 0; // 0 = all, 1 = tiles only, 2 =shaders only

Console.WriteLine("Select Action:");
Console.WriteLine("1: Tiles");
Console.WriteLine("2: Shaders");
Console.WriteLine("3: Detailed Tiles");
Console.WriteLine("Press Enter to export both.");
string actionInput = Console.ReadLine() ?? string.Empty;
_ = int.TryParse(actionInput, out action);

if(action == 0 || action == 1)
{
    Console.WriteLine("Skip base layer export and start zoom pyramid? (y/n)");
    string skipBase = Console.ReadLine() ?? string.Empty;
    if(!skipBase.Equals("y", StringComparison.CurrentCultureIgnoreCase))
    {
        Console.WriteLine("Press Enter to start export...");
        Console.ReadLine();
        var exporter = new BaseLayerExporter(map, 15, sourceDir, outputDir);
        await exporter.ExportBaseSamples();

        Console.WriteLine("Base layer export complete. Starting zoom pyramid...");
    }

    var pyramidExporter = new ZoomLayerExporter(baseZoom: 15, minZoom: 6, tilesRoot: outputDir);
    await pyramidExporter.Export();
}

if(action == 0 || action == 2)
{
    var shaderExporter = new ElevationShaderExporter(map, 6, 15, sourceDir, shaderDir);
    await shaderExporter.Export();
}

if(action == 0 || action == 3)
{
    Console.WriteLine("Press Enter to start detailed tile export...");
    Console.ReadLine();
    var detailedExporter = new DetailedTileExporter(map, 15, 17, detailedSourceDir, detailedOutputDir);
    await detailedExporter.ExportTiles();
}

class MapConfig 
{
    public required string SourceDir { get; set; }
    public required string OutputDir { get; set; }
    public required string ShaderDir { get; set; }
    public required string DetailedSourceDir { get; set; }
    public required string DetailedOutputDir { get; set; }
}

