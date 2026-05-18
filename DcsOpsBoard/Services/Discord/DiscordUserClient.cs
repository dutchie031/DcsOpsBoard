using System;
using System.Net;
using System.Text.Json.Serialization;
using DcsOpsBoard.Services.Discord.DTOs;

namespace DcsOpsBoard.Services.Discord;

public interface IDiscordUserClient
{
    public Task<DiscordClientResult<List<PartialGuild>>> GetGuildsForUserAsync();
    public Task<DiscordClientResult<List<GuildMember>>> GetGuildMembersAsync(ulong guildId, string search = "");
}

public class DiscordUserClient : IDiscordUserClient
{
    
    public static readonly string HttpClientName = "DiscordClient";
    public static readonly Action<HttpClient> ConfigureClient = client =>
    {
        client.BaseAddress = new Uri("https://discord.com/api/");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DcsOpsBoard/1.0");
    };

    public static readonly Func<HttpMessageHandler> ConfigureHandler = () => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 20,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    };

    private readonly HttpClient _httpClient;
    private readonly IUserAuthenticationState _userAuthenticationState;
    private readonly IDiscordBotClient _discordBotClient;

    public DiscordUserClient(IHttpClientFactory httpClientFactory, IUserAuthenticationState userAuthenticationState, IDiscordBotClient discordBotClient)
    {
        _httpClient = httpClientFactory.CreateClient(HttpClientName);
        _userAuthenticationState = userAuthenticationState;
        _discordBotClient = discordBotClient;
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

        var botGuilds = await _discordBotClient.GetJoinedGuildsAsync();
        if (!botGuilds.Success)
        {
            return DiscordClientResult<List<PartialGuild>>.FromError($"Failed to get connected guilds: {botGuilds.ErrorMessage}");
        }
        
        List<PartialGuild> mutualGuilds = [..guilds.Where(g => botGuilds.Data!.Any(bg => bg.Id == g.Id))];

        return DiscordClientResult<List<PartialGuild>>.FromSuccess(mutualGuilds);
    }

    public async Task<DiscordClientResult<List<GuildMember>>> GetGuildMembersAsync(ulong guildId, string search = "")
    {
        await _userAuthenticationState.EnsureLoaded();
        if(_userAuthenticationState.IsAuthenticated == false)
        {
            return DiscordClientResult<List<GuildMember>>.FromError("Not authenticated");
        }
        return await _discordBotClient.GetGuildMembersAsync(guildId, search);
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

