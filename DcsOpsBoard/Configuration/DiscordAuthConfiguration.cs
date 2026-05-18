using System;

namespace DcsOpsBoard.Configuration;

public class DiscordAuthentication
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? BotToken { get; set; }
}
