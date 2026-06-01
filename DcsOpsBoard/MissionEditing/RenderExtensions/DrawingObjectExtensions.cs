using System;
using DcsMissionParser.Net;
using DcsMissionParser.Net.CoordMapping;
using DcsMissionParser.Net.Objects.Drawing;
using DcsOpsBoard.Components.PlanningComponents.HelperClasses;
using DcsOpsBoard.Constants;
using DcsOpsBoard.MissionEditing.RenderExtensions.Context;
using DcsOpsBoard.Types.Extensions;
using OpenLayers.Blazor;

namespace DcsOpsBoard.MissionEditing.RenderExtensions;

public static class DrawingObjectExtensions
{
    public static async Task RenderAsync(this DrawingObject obj, DcsRenderContext context)
    {
        if (obj is FreeLine line)
        {
            await line.RenderAsync(context);
        } 
        else if(obj is Free polygon)
        {
            await polygon.RenderAsync(context);
        }
    }

    private static async Task RenderAsync(this FreeLine obj, DcsRenderContext context)
    {
        if(!context.RenderState.TryGetShape(obj.RefId, out Shape? cachedShape))
        {
            var layer = context.Map.LayersList.FirstOrDefault(l => l.Id == MapConstants.DrawingLayerId);
            if (layer is null)
            {
                //No drawing layer, can't render.
                return;
            }

            //No cached shape, create a new one and add it to the cache.
            cachedShape = new OpenLayers.Blazor.Line();
            context.RenderState.AddShape(obj.RefId, cachedShape);
            layer.ShapesList.Add(cachedShape);
            await layer.UpdateLayer();
        }

        if (cachedShape is not OpenLayers.Blazor.Line line)
        {
            //Weird occurance where the ref id collides with a different type of shape.
            return;
        }

        line.Points = [.. obj.Points.Select(
            p =>
            {
                DcsCoord coord = new() { X = obj.MapX+p.X, Y = obj.MapY+p.Y };
                var converted = context.CoordConverter.LOtoLL(coord);
                return new Coordinate(converted.Lon, converted.Lat);
            }
        )];

        if(DcsColorConverter.TryFromDcsStringColor(obj.ColorString, out System.Drawing.Color lineColor))
        {
            line.Stroke = $"rgba({lineColor.R}, {lineColor.G}, {lineColor.B}, {lineColor.A.ToColorDoubleString()})";
        }

        line.StrokeThickness = obj.Thickness;
               
        await line.UpdateShape();
    }

    public static async Task RenderAsync(this Free obj, DcsRenderContext context)
    {
        if(!context.RenderState.TryGetShape(obj.RefId, out Shape? cachedShape))
        {
            var layer = context.Map.LayersList.FirstOrDefault(l => l.Id == MapConstants.DrawingLayerId);
            if (layer is null)
            {
                //No drawing layer, can't render.
                return;
            }

            //No cached shape, create a new one and add it to the cache.
            cachedShape = new OpenLayers.Blazor.Polygon();
            context.RenderState.AddShape(obj.RefId, cachedShape);
            layer.ShapesList.Add(cachedShape);
            await layer.UpdateLayer();
        }

        if(cachedShape is not OpenLayers.Blazor.Polygon poly)
        {
            return;
        }

        poly.Points = [.. obj.Points.Select(
            p =>
            {
                DcsCoord coord = new() { X = obj.MapX+p.X, Y = obj.MapY+p.Y };
                var converted = context.CoordConverter.LOtoLL(coord);
                return new Coordinate(converted.Lon, converted.Lat);
            }
        )];
        
        if(DcsColorConverter.TryFromDcsStringColor(obj.ColorString, out System.Drawing.Color lineColor))
        {
            poly.Stroke = $"rgba({lineColor.R}, {lineColor.G}, {lineColor.B}, {lineColor.A.ToColorDoubleString()})";
        }

        if(DcsColorConverter.TryFromDcsStringColor(obj.FillColorString, out System.Drawing.Color fillColor))
        {
            poly.Fill = $"rgba({fillColor.R}, {fillColor.G}, {fillColor.B}, {fillColor.A.ToColorDoubleString()})";
        }

        await poly.UpdateShape();
    }
}
