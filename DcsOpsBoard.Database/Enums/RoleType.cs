using System;

namespace DcsOpsBoard.Database.Enums;

public enum RoleType
{
    Unknown = 0,
    Admin = 1,
    Editor = 2,
    BlueFlightLead = 3,
    RedFlightLead = 4,
    Viewer = 5,
}

public static class RoleTypeExtensions
{
    public static RoleType GetHighestRole(this IEnumerable<RoleType> roles)
    {
        if (roles is null || !roles.Any())
            return RoleType.Unknown;

        var bestRole = RoleType.Unknown;

        foreach (var role in roles)
        {
            if (role == RoleType.Unknown)
                continue;

            if (bestRole == RoleType.Unknown || role < bestRole)
                bestRole = role;
        }

        return bestRole;
    }
}
