using System;

namespace DcsOpsBoard.Database.Configuration;

public class DatabaseConfiguration
{
    public required string Path { get; set; }

    public required string MissionFilesPath { get; set; }
}
