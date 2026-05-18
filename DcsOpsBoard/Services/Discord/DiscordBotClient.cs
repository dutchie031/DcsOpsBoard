using System;
using System.Net;
using DcsOpsBoard.Configuration;
using DcsOpsBoard.Services.Discord.DTOs;
using Microsoft.Extensions.Options;

namespace DcsOpsBoard.Services.Discord;

public interface IDiscordBotClient
{
    Task<DiscordClientResult<List<PartialGuild>>> GetJoinedGuildsAsync();
    Task<DiscordClientResult<List<GuildMember>>> GetGuildMembersAsync(ulong guildId, string search = "");
    Task<DiscordClientResult<GuildMember?>> GetGuildMemberAsync(ulong guildId, ulong userId);
}

public class DiscordBotClient : IDiscordBotClient
{
    private readonly HttpClient _httpClient;
    private readonly DiscordAuthentication _configuration;

    public DiscordBotClient(
        IHttpClientFactory httpClientFactory,
        IOptions<DiscordAuthentication> configuration)
    {
        _httpClient = httpClientFactory.CreateClient(HttpClientName);
        _configuration = configuration.Value;
    }

    public static readonly string HttpClientName = "DiscordBotClient";
    public static readonly Action<HttpClient> ConfigureClient = client =>
    {
        client.BaseAddress = new Uri("https://discord.com/api/v10/");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DcsOpsBoard/1.0");
    };

    public static readonly Func<HttpMessageHandler> ConfigureHandler = () => new SocketsHttpHandler();

    
    public async Task<DiscordClientResult<List<PartialGuild>>> GetJoinedGuildsAsync()
    {
        //Can cache this quite aggressively.
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "users/@me/guilds");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bot", _configuration.BotToken);

        HttpResponseMessage response = await _httpClient.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            var guilds = System.Text.Json.JsonSerializer.Deserialize<List<PartialGuild>>(content);
            return DiscordClientResult<List<PartialGuild>>.FromSuccess(guilds ?? []);
        }
        else
        {
            return DiscordClientResult<List<PartialGuild>>.FromError($"Discord API returned error: {response.StatusCode}");
        }
    }

    public async Task<DiscordClientResult<List<GuildMember>>> GetGuildMembersAsync(ulong guildId, string search = "")
    {        
        using var request = new HttpRequestMessage(HttpMethod.Get, $"guilds/{guildId}/members/search?query={Uri.EscapeDataString(search)}&limit=10");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bot", _configuration.BotToken);

        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)        
        {
            string errorContent = await response.Content.ReadAsStringAsync();
            return DiscordClientResult<List<GuildMember>>.FromError($"Discord API returned error: {response.StatusCode}");
        }

        var members = await response.Content.ReadFromJsonAsync<List<GuildMember>>();
        if (members == null)
        {
            return DiscordClientResult<List<GuildMember>>.FromError("Failed to parse Discord API response");
        }
        return DiscordClientResult<List<GuildMember>>.FromSuccess(members);
    }

    public Task<DiscordClientResult<GuildMember?>> GetGuildMemberAsync(ulong guildId, ulong userId)
    {
        throw new NotImplementedException();
    }
}