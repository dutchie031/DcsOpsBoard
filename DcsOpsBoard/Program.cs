using System.Net;
using AspNet.Security.OAuth.Discord;
using DcsOpsBoard.Components;
using DcsOpsBoard.Components.Modals.AreYouSure;
using DcsOpsBoard.Components.Modals.Discord;
using DcsOpsBoard.Components.Modals.Warnings;
using DcsOpsBoard.Configuration;
using DcsOpsBoard.Database;
using DcsOpsBoard.Hubs;
using DcsOpsBoard.Hubs.Clients;
using DcsOpsBoard.Hubs.MissionSync;
using DcsOpsBoard.MissionEditing;
using DcsOpsBoard.Services;
using DcsOpsBoard.Services.Discord;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

var services = builder.Services;

services.AddScoped<IUserMapSettings, UserMapSettings>();

services.AddHttpClient();
services.AddHttpContextAccessor();
services.AddScoped<IUserAuthenticationState, UserAuthenticationState>();
services.AddScoped<AreYouSureService>();
services.AddScoped<WarningService>();
services.AddScoped<IMissionSelectorService, MissionSelectorService>();

//Discord scoped services
services.AddHttpClient(DiscordUserClient.HttpClientName, DiscordUserClient.ConfigureClient).ConfigurePrimaryHttpMessageHandler(DiscordUserClient.ConfigureHandler);
services.AddHttpClient(DiscordBotClient.HttpClientName, DiscordBotClient.ConfigureClient).ConfigurePrimaryHttpMessageHandler(DiscordBotClient.ConfigureHandler);

services.Configure<DiscordAuthentication>(builder.Configuration.GetSection("DiscordAuthentication"));
services.AddScoped<IDiscordUserClient, DiscordUserClient>();
services.AddSingleton<IDiscordBotClient, DiscordBotClient>();
services.AddScoped<IFindUserService, FindUserService>();

builder.Services.Configure<TileConfiguration>(builder.Configuration.GetSection("TileConfiguration"));

builder.Services.AddSingleton<IMissionCache, MissionCache>();
builder.Services.AddHostedService(sp => (sp.GetRequiredService<IMissionCache>() as MissionCache)!);

builder.Services.AddSingleton<MissionCommandQueue>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MissionCommandQueue>());

builder.Services.AddScoped<HubConnectionProvider<CursorHub>>();
builder.Services.AddScoped<IMissionEditingClient, MissionEditingClient>();
builder.Services.AddScoped<HubConnectionProvider<MissionEditHub>>();

// Add services to the container.
builder.Services.AddRazorComponents() 
    .AddInteractiveServerComponents();

builder.Services.AddControllers();
builder.Services.AddSignalR();

builder.Services.RegisterDatabase(builder.Configuration);



#region Authentication

services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = DiscordAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.CookieManager = new ChunkingCookieManager();

    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.None;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.SlidingExpiration = true;

    options.ExpireTimeSpan = TimeSpan.FromDays(14);
})
.AddDiscord(DiscordAuthenticationDefaults.AuthenticationScheme, options =>
{
    DiscordAuthentication discordAuthConfig = builder.Configuration.GetSection("DiscordAuthentication").Get<DiscordAuthentication>() ?? throw new Exception("Failed to load Discord authentication configuration");

    if(string.IsNullOrEmpty(discordAuthConfig.ClientId) || string.IsNullOrEmpty(discordAuthConfig.ClientSecret))
    {
        throw new Exception("Discord authentication configuration is missing ClientId or ClientSecret");
    }

    options.ClientId = discordAuthConfig.ClientId;
    options.ClientSecret = discordAuthConfig.ClientSecret;

    options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;

    options.Scope.Add("identify");
    options.Scope.Add("guilds");
    options.Scope.Add("guilds.members.read");

    options.SaveTokens = true;

    options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
    options.CorrelationCookie.SameSite = SameSiteMode.None;
});

#endregion

    var app = builder.Build();

await app.Services.InitializeDatabaseAsync();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapControllers();
app.MapHub<MissionEditHub>(MissionEditHub.HubUrl);
app.MapHub<CursorHub>(CursorHub.HubUrl);

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

internal class MissionCommandProcessor
{
}