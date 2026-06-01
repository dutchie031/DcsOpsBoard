using DcsMissionParser.Net;
using DcsMissionParser.Net.CoordMapping;
using DcsOpsBoard.Types;

namespace _2dTileExporter.Utils;

public static class CoordConversion
{
    // Global lock to serialize calls into native CoordConverter.LOtoLL which is not thread-safe.
    private static readonly Lock loToLlLock = new();

    public static LatLong SafeLOtoLL(CoordConverter converter, DcsCoord coord)
    {
        lock (loToLlLock)
        {
            return converter.LOtoLL(coord);
        }
    }
}
