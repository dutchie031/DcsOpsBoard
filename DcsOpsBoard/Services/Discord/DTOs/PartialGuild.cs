using System;
using System.Text.Json.Serialization;

namespace DcsOpsBoard.Services.Discord.DTOs;

public class PartialGuild
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("approximate_member_count")]
    public int? ApproximateMemberCount { get; set; }

    public string IconUrl => Icon != null ? $"https://cdn.discordapp.com/icons/{Id}/{Icon}.png" : "https://cdn.discordapp.com/embed/avatars/0.png";
}
