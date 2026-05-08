using System;
using DcsOpsBoard.Types;

namespace _2dTileExporter.Readers;

public class TerrainTypeReader : IDisposable
{
    private readonly FileStream _fileStream;
    private readonly BinaryReader _binaryReader;

    public TerrainTypeReader(string filePath)
    {
        _fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        _binaryReader = new BinaryReader(_fileStream);
    }

    public static TerrainType[] ReadAll(string filePath)
    {
        return [..File.ReadAllBytes(filePath).Select(b => b.ToTerrainType())];
    }

    public TerrainType? ReadNextTerrainType()
    {
        try
        {
            byte terrainType = _binaryReader.ReadByte();
            return terrainType.ToTerrainType();
        }
        catch (EndOfStreamException)
        {
            return null; // Indicate end of stream
        }
    }

    public void Reset()
    {
        _fileStream.Seek(0, SeekOrigin.Begin);
    }

    public void Skip(int count)
    {
        _fileStream.Seek(count, SeekOrigin.Current);
    }

    public void Dispose()
    {
        _binaryReader?.Dispose();
        _fileStream?.Dispose();
    }
}
