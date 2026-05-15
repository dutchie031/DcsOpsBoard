using System;
using System.ComponentModel.DataAnnotations.Schema;
using DcsMissionParser.Net.Objects.Coalitions.Routes.Plane.Tasks;
using DcsOpsBoard.Database.Enums;
using Microsoft.EntityFrameworkCore;

namespace DcsOpsBoard.Database.Entities;

public class Permission
{
    public ulong UserId { get; set; }

    public Guid MissionId { get; set; }

    public RoleType Role { get; set; }

}
