using System;

namespace DcsOpsBoard.Types;

public enum MissionType
{
    Singleplayer = 1,
    Multiplayer = 2
}

public static class MissionTypeEnum
{
    public static Dictionary<string, MissionType> GetPickerDictionary()
    {
        return Enum.GetValues<MissionType>().ToDictionary(e => e.ToString(), e => e);
    }
}