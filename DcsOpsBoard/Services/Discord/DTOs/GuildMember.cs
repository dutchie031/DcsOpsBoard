using System;
using System.Text.Json.Serialization;
using Lua.CodeAnalysis.Syntax.Nodes;

namespace DcsOpsBoard.Services.Discord.DTOs;

public class GuildMember
{
    [JsonPropertyName("user")]
    public DiscordUser? User { get; set; }  

    [JsonPropertyName("nick")]
    public string? Nickname { get; set; }

    [JsonPropertyName("avatar")]
    public string? Avatar { get; set; }

    public string DisplayName => Nickname ?? User?.DisplayName ?? "Unknown User";
    public string AvatarUrl => User?.AvatarUrl ?? "https://cdn.discordapp.com/embed/avatars/0.png";
}
