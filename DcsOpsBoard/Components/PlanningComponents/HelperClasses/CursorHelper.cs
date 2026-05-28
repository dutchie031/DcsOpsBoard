using System;
using DcsOpsBoard.Hubs;
using DcsOpsBoard.Hubs.HubModels;
using DcsOpsBoard.MissionEditing;
using DcsOpsBoard.Services;
using Microsoft.AspNetCore.SignalR.Client;
using OpenLayers.Blazor;

namespace DcsOpsBoard.Components.PlanningComponents.HelperClasses;

public class CursorHelper
{
    public CursorData? _myCursorData;
    private bool _registeredCursorHandler = false;

    public required IUserAuthenticationState AuthState { get; init; }
    public required HubConnectionProvider<CursorHub> CursorHubProvider { get; init; }
    public required IUserMapSettings UserMapSettings { get; init; }
    public required Layer CursorLayer { get; init; }
    private readonly Dictionary<string, Shape> _cursorShapes = [];
    
    private Guid _missionId = Guid.Empty;    

    public async Task JoinCursorsHub(Guid missionId)
    {
        await AuthState.EnsureLoaded();
        if (!AuthState.IsAuthenticated || AuthState.DiscordId is null)
        {
            return;
        }

        _missionId = missionId;

        _myCursorData = new CursorData
        {
            UserId = AuthState.DiscordId.Value,
            MissionId = missionId,
            UserName = AuthState.Username ?? "Unknown",
            AvatarUrl = AuthState.AvatarUrl ?? string.Empty,
            Lat = 0,
            Lon = 0
        };

        await CursorHubProvider.EnsureStartedAsync();

        if (!_registeredCursorHandler)
        {
            CursorHubProvider.Connection.On<CursorData>(CursorHub.ReceivedCursorPositionMethodName, async (data) =>
            {
                if (!UserMapSettings.ShowFriendlyCursors)
                {
                    return;
                }

                _ = UpdateCursor(data);
            });
            _registeredCursorHandler = true;
        }
        ;

        await CursorHubProvider.Connection.SendAsync(CursorHub.JoinCursorGroupMethodName, _missionId, AuthState.DiscordId);
    }

    public async Task UpdateCursor(CursorData data)
    {
        if(CursorLayer is null)
            return;


        var cursorId = "cursor-" + data.UserId;

        if (!_cursorShapes.TryGetValue(cursorId, out var shape))
        {
            shape = new Shape(ShapeType.Point)
            {
                Id = cursorId,
                Coordinates = new Coordinates(new Coordinate(data.Lon, data.Lat)),
                Styles = [
                    new()
                    {
                        Circle = new StyleOptions.CircleStyleOptions
                        {
                            Radius = 10,
                            Fill = new StyleOptions.FillOptions
                            {
                                Color = "#222430"
                            },
                            Stroke = new StyleOptions.StrokeOptions
                            {
                                Color = "#222430",
                                Width = 2
                            },
                            Opacity = 0.7
                        }
                    },
                    new ()
                    {
                        Icon = new StyleOptions.IconStyleOptions
                        {
                            AnchorOrigin = StyleOptions.IconOrigin.TopRight,
                            AnchorXUnits = StyleOptions.IconAnchorUnits.Fraction,
                            AnchorYUnits = StyleOptions.IconAnchorUnits.Fraction,
                            Anchor = new double[] { 0.5, 0.5 },
                            Opacity = 1,
                            Width = 16,
                            Height = 16,
                            Source = data.AvatarUrl,
                        }
                    }

                ]
            };

            _cursorShapes[cursorId] = shape;
            CursorLayer.ShapesList.Add(shape);
            await CursorLayer.UpdateLayer();
            return;
        }

        shape.Coordinates = new Coordinates(new Coordinate(data.Lon, data.Lat));
        await shape.UpdateCoordinates();
    }

    private DateTime _lastPointerSent= DateTime.MinValue;
    private readonly TimeSpan PointerUpdateInterval = TimeSpan.FromMilliseconds(100);
    
    public async Task SignalPointerMove(Coordinate coordinate) 
    {
        if(_myCursorData is null || _missionId == Guid.Empty)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _lastPointerSent < PointerUpdateInterval)
        {
            return;
        }

        if(CursorHubProvider.Connection.State != HubConnectionState.Connected)
        {
            return;
        }

        _lastPointerSent = now;
        _myCursorData.Lat = coordinate.Y;
        _myCursorData.Lon = coordinate.X;

        await CursorHubProvider.Connection.SendAsync(CursorHub.SendCursorPositionMethodName, _missionId, _myCursorData);
    }
}
