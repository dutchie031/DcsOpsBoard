using System;
using DcsMissionParser.Net;
using DcsMissionParser.Net.Objects.Coalitions.Countries.Groups;
using DcsOpsBoard.Components.PlanningComponents.HelperClasses;
using DcsOpsBoard.Database.Enums;
using DcsOpsBoard.Hubs.Clients;
using DcsOpsBoard.MissionEditing.RenderExtensions.Context;
using DcsOpsBoard.Types.Enums;
using OpenLayers.Blazor;

namespace DcsOpsBoard.MissionEditing.RenderExtensions;

public static class DcsMissionExtensions
{
    public static async Task RenderAsync(this DcsMission mission, DcsRenderContext context)
    {
        if(mission == null)
        {
            return;
        }

        if(mission.Coalitions.Blue != null)
        {
            foreach(PlaneGroup group in mission.Coalitions.Blue.Countries.SelectMany(x => x.Planes.Groups))
            {
                await group.RenderAsync(context);
            }
        }

        if(mission.Coalitions.Red != null)
        {
            foreach(PlaneGroup group in mission.Coalitions.Red.Countries.SelectMany(x => x.Planes.Groups))
            {
                await group.RenderAsync(context);
            }
        }

        if(mission.Drawings != null)
        {
            await mission.Drawings.RenderAsync(context);
        }
        
    }

    private static async Task RenderAsync(this DcsMissionParser.Net.Objects.Drawing.Drawings drawings, DcsRenderContext context)
    {
        RoleType role = context.EditingClient.CurrentRoleInMission;
        foreach(DcsMissionParser.Net.Objects.Drawing.Layer layer in drawings.Layers)
            {
                if(layer.Name?.Equals("common", StringComparison.OrdinalIgnoreCase) == true)
                {
                    foreach(DcsMissionParser.Net.Objects.Drawing.DrawingObject obj in layer.Objects)
                    {
                        await obj.RenderAsync(context);
                    }
                    
                } else if(layer.Name?.Equals("blue", StringComparison.OrdinalIgnoreCase) == true && role is RoleType.BlueFlightLead or RoleType.Admin or RoleType.Editor)
                {
                    foreach(DcsMissionParser.Net.Objects.Drawing.DrawingObject obj in layer.Objects)
                    {
                        await obj.RenderAsync(context);
                    }
                }
                else if(layer.Name?.Equals("red", StringComparison.OrdinalIgnoreCase) == true && role is RoleType.RedFlightLead or RoleType.Admin or RoleType.Editor)
                {
                    foreach(DcsMissionParser.Net.Objects.Drawing.DrawingObject obj in layer.Objects)
                    {
                        await obj.RenderAsync(context);
                    }
                } 
                else if(layer.Name?.Equals("author", StringComparison.OrdinalIgnoreCase) == true && role is RoleType.Admin or RoleType.Editor)
                {
                    foreach(DcsMissionParser.Net.Objects.Drawing.DrawingObject obj in layer.Objects)
                    {
                        await obj.RenderAsync(context);
                    }
                }
            }
    }
}
