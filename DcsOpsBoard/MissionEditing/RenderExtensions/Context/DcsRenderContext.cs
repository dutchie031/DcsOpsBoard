using System;
using DcsMissionParser.Net;
using DcsOpsBoard.Components.PlanningComponents.HelperClasses;
using DcsOpsBoard.Hubs.Clients;
using DcsOpsBoard.Hubs.MissionSync;
using DcsOpsBoard.Types;

namespace DcsOpsBoard.MissionEditing.RenderExtensions.Context;

/// <summary>
/// Context object for rendering objects on the map.
/// </summary>
public class DcsRenderContext
{
    public required IMissionEditingClient EditingClient { get; init; }
    public required OpenLayers.Blazor.Map Map { get; init; }
    public required MissionRenderState RenderState { get; init; }
    public required CoordConverter CoordConverter { get; set; }
    public required IMissionCache MissionCache { get; init; }
    public Guid SelectedRefId { get; set; } = Guid.Empty;
}
