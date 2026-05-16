using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace DcsOpsBoard.Hubs;

public class HubConnectionProvider<T> : IAsyncDisposable where T : IBaseHub
    {
        public HubConnection Connection { get; }

        public HubConnectionProvider(NavigationManager nav)
        {
            Connection = new HubConnectionBuilder()
                .WithUrl(nav.ToAbsoluteUri(T.HubUrl))
                .WithAutomaticReconnect()
                .Build();
        }

        public async ValueTask DisposeAsync()
        {
            if (Connection != null)
            {
                await Connection.DisposeAsync();
            }
        }

        public async Task EnsureStartedAsync()
        {
            if (Connection.State == HubConnectionState.Disconnected)
            {
                await Connection.StartAsync();
            }
        }
    }