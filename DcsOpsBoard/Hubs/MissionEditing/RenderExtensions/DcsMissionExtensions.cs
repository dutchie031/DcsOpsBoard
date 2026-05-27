using System;
using DcsMissionParser.Net;
using DcsMissionParser.Net.Objects.Coalitions.Countries.Groups;
using DcsOpsBoard.Database.Enums;
using DcsOpsBoard.Types.Enums;
using OpenLayers.Blazor;

namespace DcsOpsBoard.Hubs.MissionEditing.RenderExtensions;

public static class DcsMissionExtensions
{
    public static async Task RenderAsync(this DcsMission mission, RoleType role, List<string> ownedFlights, Map map, Dictionary<Guid, Shape> renderCache, CoordConverter coordConverter)
    {
        if(mission == null)
        {
            return;
        }

        if(mission.Coalitions.Blue != null)
        {
            foreach(PlaneGroup group in mission.Coalitions.Blue.Countries.SelectMany(x => x.Planes.Groups))
            {
                await group.RenderAsync(CoalitionSide.Blue, role, ownedFlights, map, renderCache, coordConverter);
            }
        }

        if(mission.Coalitions.Red != null)
        {
            foreach(PlaneGroup group in mission.Coalitions.Red.Countries.SelectMany(x => x.Planes.Groups))
            {
                await group.RenderAsync(CoalitionSide.Red, role, ownedFlights, map, renderCache, coordConverter);
            }
        }

        if(mission.Drawings != null)
        {
            await mission.Drawings.RenderAsync(role, map, renderCache, coordConverter);
        }
        
    }

    private static async Task RenderAsync(this DcsMissionParser.Net.Objects.Drawing.Drawings drawings, RoleType role, Map map, Dictionary<Guid, Shape> renderCache, CoordConverter coordConverter)
    {
        foreach(DcsMissionParser.Net.Objects.Drawing.Layer layer in drawings.Layers)
            {
                if(layer.Name?.Equals("common", StringComparison.OrdinalIgnoreCase) == true)
                {
                    foreach(DcsMissionParser.Net.Objects.Drawing.DrawingObject obj in layer.Objects)
                    {
                        await obj.RenderAsync(map, renderCache, coordConverter);
                    }
                    
                } else if(layer.Name?.Equals("blue", StringComparison.OrdinalIgnoreCase) == true && role is RoleType.BlueFlightLead or RoleType.Admin or RoleType.Editor)
                {
                    foreach(DcsMissionParser.Net.Objects.Drawing.DrawingObject obj in layer.Objects)
                    {
                        await obj.RenderAsync(map, renderCache, coordConverter);
                    }
                }
                else if(layer.Name?.Equals("red", StringComparison.OrdinalIgnoreCase) == true && role is RoleType.RedFlightLead or RoleType.Admin or RoleType.Editor)
                {
                    foreach(DcsMissionParser.Net.Objects.Drawing.DrawingObject obj in layer.Objects)
                    {
                        await obj.RenderAsync(map, renderCache, coordConverter);
                    }
                } 
                else if(layer.Name?.Equals("author", StringComparison.OrdinalIgnoreCase) == true && role is RoleType.Admin or RoleType.Editor)
                {
                    foreach(DcsMissionParser.Net.Objects.Drawing.DrawingObject obj in layer.Objects)
                    {
                        await obj.RenderAsync(map, renderCache, coordConverter);
                    }
                }
            }
    }
}
