using System;
using System.Text.Json.Serialization;

namespace DcsOpsBoard.Services.Discord;

public interface IDiscordClient
{
    
}

public class DiscordClient : IDiscordClient
{
    
    public static readonly string HttpClientName = "DiscordClient";
    public static readonly Action<HttpClient> ConfigureClient = client =>
    {
        client.BaseAddress = new Uri("https://discord.com/api/");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DcsOpsBoard/1.0");
    };

    private readonly HttpClient _httpClient;
    private readonly IUserAuthenticationState _userAuthenticationState;

    public DiscordClient(IHttpClientFactory httpClientFactory, IUserAuthenticationState userAuthenticationState)
    {
        _httpClient = httpClientFactory.CreateClient(HttpClientName);
        _userAuthenticationState = userAuthenticationState;
    }

    public async Task<DiscordClientResult<List<PartialGuild>>> GetGuildsForUserAsync()
    {
        await _userAuthenticationState.EnsureLoaded();

        string? access_token = await _userAuthenticationState.GetAccessToken();
        if (access_token == null)
        {
            return DiscordClientResult<List<PartialGuild>>.FromError("Not authenticated");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "users/@me/guilds");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access_token);

        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)        {
            return DiscordClientResult<List<PartialGuild>>.FromError($"Discord API returned error: {response.StatusCode}");
        }

        var guilds = await response.Content.ReadFromJsonAsync<List<PartialGuild>>();
        if (guilds == null)
        {
            return DiscordClientResult<List<PartialGuild>>.FromError("Failed to parse Discord API response");
        }

        return DiscordClientResult<List<PartialGuild>>.FromSuccess(guilds);
    }

    public async Task<DiscordClientResult<List<GuildMember>>> GetGuildMembersAsync(string guildId)
    {
        await _userAuthenticationState.EnsureLoaded();

        string? access_token = await _userAuthenticationState.GetAccessToken();
        if (access_token == null)
        {
            return DiscordClientResult<List<GuildMember>>.FromError("Not authenticated");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"guilds/{guildId}/members?limit=1000");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access_token);

        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)        {
            return DiscordClientResult<List<GuildMember>>.FromError($"Discord API returned error: {response.StatusCode}");
        }

        var members = await response.Content.ReadFromJsonAsync<List<GuildMember>>();
        if (members == null)
        {
            return DiscordClientResult<List<GuildMember>>.FromError("Failed to parse Discord API response");
        }

        return DiscordClientResult<List<GuildMember>>.FromSuccess(members);
    }
}

public class DiscordClientResult<T>
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }  
    public T? Data { get; set; }
    public static DiscordClientResult<T> FromSuccess(T data) => new() { Success = true, Data = data };
    public static DiscordClientResult<T> FromError(string errorMessage) => new() { Success = false, ErrorMessage = errorMessage };

}

public class PartialGuild
{
    [JsonPropertyName("id")]
    public required ulong Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("approximate_member_count")]
    public int? ApproximateMemberCount { get; set; }
}

public class GuildMember
{
    [JsonPropertyName("user")]
    public DiscordUser? User { get; set; }  

    [JsonPropertyName("nick")]
    public string? Nickname { get; set; }

    [JsonPropertyName("avatar")]
    public string? Avatar { get; set; }
}

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
}
