using System;

namespace DcsOpsBoard.Hubs.HubModels;

public class CursorData
{
    public required Guid MissionId { get; set; }
    public required double Lat { get; set; }
    public required double Lon { get; set; }
    public required string UserId { get; set; }
    public required string UserName { get; set; }
    public required string AvatarUrl { get; set; }
}
