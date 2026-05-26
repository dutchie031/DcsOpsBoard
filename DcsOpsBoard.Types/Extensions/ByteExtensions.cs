using System;

namespace DcsOpsBoard.Types.Extensions;

public static class ByteExtensions
{
    public static double ToColorDouble(this byte b)
    {
        return b / 255.0;
    }
}
