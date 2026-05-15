using System;
using DcsOpsBoard.Database.Context;
using DcsOpsBoard.Database.Entities;
using DcsOpsBoard.Database.Enums;
using Microsoft.EntityFrameworkCore;

namespace DcsOpsBoard.Database.Services;

public interface IPermissionManager
{
    /// <summary>
    /// Assigns a permission to a user for a specific mission. If the permission already exists, it will not be added again.
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="missionId"></param>
    /// <param name="role"></param>
    void AssignPermission(ulong userId, Guid missionId, RoleType role);

    /// <summary>
    /// Removes a permission from a user for a specific mission. If the permission does not exist, no action is taken.
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="missionId"></param>
    /// <param name="role"></param>
    void RemovePermission(ulong userId, Guid missionId, RoleType role);
        
    /// <summary>
    /// Retrieves the permissions for a specific mission. If a user ID is provided, only the permissions for that user will be returned. Otherwise, all permissions for the mission will be returned.
    /// </summary>
    /// <param name="missionId"></param>
    /// <param name="user"></param>
    /// <returns></returns>
    List<Permission> GetPermissionsForMission(Guid missionId, ulong? user = null);
}

public class PermissionManager(IDbContextFactory<OpsBoardDbContext> _dbContextFactory) : IPermissionManager
{
    public void AssignPermission(ulong userId, Guid missionId, RoleType role)
    {
        using OpsBoardDbContext dbContext = _dbContextFactory.CreateDbContext();

        if(dbContext.Permissions.Any(p => p.UserId == userId && p.MissionId == missionId && p.Role == role))
        {
            return; // Permission already exists, no need to add again
        }

        Permission permission = new()
        {
            UserId = userId,
            MissionId = missionId,
            Role = role
        };

        dbContext.Permissions.Add(permission);
        dbContext.SaveChanges();
    }

    public List<Permission> GetPermissionsForMission(Guid missionId, ulong? user = null)
    {
        using OpsBoardDbContext dbContext = _dbContextFactory.CreateDbContext();

        if (user.HasValue)
        {
            return [..dbContext.Permissions.Where(p => p.MissionId == missionId && p.UserId == user.Value)];
        }
        else
        {
            return [..dbContext.Permissions.Where(p => p.MissionId == missionId)];
        }
    }

    public void RemovePermission(ulong userId, Guid missionId, RoleType role)
    {
        using OpsBoardDbContext dbContext = _dbContextFactory.CreateDbContext();
        Permission? permission = dbContext.Permissions.FirstOrDefault(p => p.UserId == userId && p.MissionId == missionId && p.Role == role);

        if (permission != null)
        {
            dbContext.Permissions.Remove(permission);
            dbContext.SaveChanges();
        }
    }
    
}
