using System;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Http.Logging;

namespace DcsOpsBoard.Services;

public interface IUserAuthenticationState
{
    Task EnsureLoaded();

    public event Action OnAuthenticationStateChanged;

    public bool IsAuthenticated { get; }

    public string? Username { get; }

    public ulong? DiscordId { get; }

    public string AvatarUrl { get; }

}

public class UserAuthenticationState : IUserAuthenticationState
{
    public event Action OnAuthenticationStateChanged = () => { };

    public bool IsAuthenticated { get; private set; } = false;

    public string? Username { get; private set; }

    public ulong? DiscordId { get; private set; }

    public string AvatarUrl { 
        get
        {
            if(field == null)
            {
                // Return a default avatar URL or null if not authenticated
                return "";
            }
            return field;
        }
        private set; } = null!;

    private bool _isLoaded = false;

    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public UserAuthenticationState(
        AuthenticationStateProvider authenticationStateProvider, 
        IHttpClientFactory httpClientFactory,
        IHttpContextAccessor httpContextAccessor)
    {
        _authenticationStateProvider = authenticationStateProvider;
        _httpClientFactory = httpClientFactory;
        _httpContextAccessor = httpContextAccessor;
        _authenticationStateProvider.AuthenticationStateChanged += AuthenticationStateChanged;
    }

    private async void AuthenticationStateChanged(Task<AuthenticationState> task)
    {
        var authState = await task;
        if(authState.User.Identity?.IsAuthenticated ?? false)
        {
            await FetchData();
        }
        else
        {
            IsAuthenticated = false;
            Username = null;
            DiscordId = null;
            AvatarUrl = null!;
        }
        OnAuthenticationStateChanged.Invoke();
    }

    public async Task EnsureLoaded()
    {
        if(_isLoaded) return;
        await FetchData();    
        
        _isLoaded = true;
    }

    public async Task FetchData()
    {
        var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
        IsAuthenticated = authState.User.Identity?.IsAuthenticated ?? false;
        Username = authState.User.Identity?.Name;

        string? accessToken = await _httpContextAccessor.HttpContext!.GetTokenAsync("access_token");
        if (accessToken == null)
        {
            return;
        }

        HttpClient client = _httpClientFactory.CreateClient(nameof(UserAuthenticationState));
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        
        HttpResponseMessage responseMessage = await client.GetAsync("https://discord.com/api/oauth2/@me");
        if (responseMessage.StatusCode != System.Net.HttpStatusCode.OK)
        {
            return;
        }

        string responseContent = await responseMessage.Content.ReadAsStringAsync();
        var response = System.Text.Json.JsonSerializer.Deserialize<DiscordOauthResponse>(responseContent);

        if (response?.User != null)
        {
            DiscordId = ulong.Parse(response.User.Id);
            Username = response.User.GlobalName ?? response.User.Username;
            AvatarUrl = $"https://cdn.discordapp.com/avatars/{response.User.Id}/{response.User.Avatar}.png";
        }

        _isLoaded = true;
    }

    private class DiscordOauthResponse
    {
        [JsonPropertyName("user")]
        public required DiscordOauthUserField User { get; set; }
    }

    private class DiscordOauthUserField
    {
        [JsonPropertyName("id")]
        public required string Id { get; set; }
        [JsonPropertyName("username")]
        public required string Username { get; set; }

        [JsonPropertyName("avatar")]
        public required string Avatar { get; set; }

        [JsonPropertyName("global_name")]
        public string? GlobalName { get; set; }

    }

}
