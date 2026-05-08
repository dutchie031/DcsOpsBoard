using System;
using System.ComponentModel.DataAnnotations;
using DcsMissionParser.Net;

namespace DcsOpsBoard.Database.Entities;

public class OpsPlanningMission
{
    [Key]
    public Guid MissionId { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required MizObject MizObject { get; set; }
}
