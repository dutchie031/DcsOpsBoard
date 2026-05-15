using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DcsMissionParser.Net;

namespace DcsOpsBoard.Database.Entities;

public class OpsPlanningMission
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid MissionId { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required ulong OwnerId { get; set; }

    public required string UploadedMissionName { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastEditedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Permission> Permissions { get; set; } = [];
    public ICollection<UserFlights> UserFlights { get; set; } = [];
}
