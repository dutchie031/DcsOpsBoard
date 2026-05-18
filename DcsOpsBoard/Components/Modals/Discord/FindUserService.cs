using System;
using DcsOpsBoard.Services.Discord.DTOs;

namespace DcsOpsBoard.Components.Modals.Discord;

public interface IFindUserService
{
    public Func<Task<DiscordUser?>>? OnFindUserRequested { get; set; }
    public Task<DiscordUser?> FindUserAsync();
}

public class FindUserService : IFindUserService
{
    public Func<Task<DiscordUser?>>? OnFindUserRequested { get; set; }

    public async Task<DiscordUser?> FindUserAsync()
    {
        if (OnFindUserRequested != null)
        {
            return await OnFindUserRequested.Invoke();
        }

        return null;
    }
}

