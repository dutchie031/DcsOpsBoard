using System;
using System.Text.Json.Serialization;

namespace DcsOpsBoard.Services.Discord.DTOs;

public class DiscordUser
{
    [JsonPropertyName("id")]
    public required ulong Id { get; set; }

    [JsonPropertyName("username")]
    public required string Username { get; set; }

    [JsonPropertyName("avatar")]
    public string? Avatar { get; set; }

    [JsonPropertyName("global_name")]
    public string? GlobalName { get; set; }

    public string? DisplayName => GlobalName ?? Username;
    public string? AvatarUrl => Avatar != null ? $"https://cdn.discordapp.com/avatars/{Id}/{Avatar}.png" : "https://cdn.discordapp.com/embed/avatars/0.png";
}
