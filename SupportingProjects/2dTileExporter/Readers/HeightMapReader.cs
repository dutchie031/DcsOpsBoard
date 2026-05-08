using System;

namespace _2dTileExporter.Readers;

public sealed class HeightMapReader : IDisposable
{
    private readonly FileStream _fileStream;
    private readonly BinaryReader _binaryReader;

    public HeightMapReader(string filePath)
    {
        _fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        _binaryReader = new BinaryReader(_fileStream);
    }

    public static int[] ReadAll(string filePath)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        if (bytes.Length % sizeof(int) != 0)
        {
            throw new InvalidDataException($"Height map file has invalid length: {filePath}");
        }

        int[] heights = new int[bytes.Length / sizeof(int)];
        for (int i = 0; i < heights.Length; i++)
        {
            heights[i] = BitConverter.ToInt32(bytes, i * sizeof(int));
        }

        return heights;
    }

    public int? ReadNextHeight()
    {
        try
        {
            return _binaryReader.ReadInt32();
        }
        catch (EndOfStreamException)
        {
            return null;
        }
    }

    public void Reset()
    {
        _fileStream.Seek(0, SeekOrigin.Begin);
    }

    public void Dispose()
    {
        _binaryReader.Dispose();
        _fileStream.Dispose();
    }
}