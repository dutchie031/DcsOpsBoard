using System;
using Microsoft.AspNetCore.SignalR;

namespace DcsOpsBoard.Hubs;

public interface IBaseHub
{
    public abstract static string HubUrl { get;}
}

