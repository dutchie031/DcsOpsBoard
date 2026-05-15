using AspNet.Security.OAuth.Discord;
using DcsOpsBoard.Components;
using DcsOpsBoard.Configuration;
using DcsOpsBoard.Database;
using DcsOpsBoard.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

var services = builder.Services;

services.AddScoped<IUserMapSettings, UserMapSettings>();

services.AddHttpClient();
services.AddHttpContextAccessor();
services.AddScoped<IUserAuthenticationState, UserAuthenticationState>();

builder.Services.Configure<TileConfiguration>(builder.Configuration.GetSection("TileConfiguration"));

builder.Services.AddSingleton<MissionCommandQueue>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MissionCommandQueue>());

// Add services to the container.
builder.Services.AddRazorComponents() 
    .AddInteractiveServerComponents();

builder.Services.AddControllers();

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
    options.ClientId = builder.Configuration["DiscordAuthentication:ClientId"] ?? throw new InvalidOperationException("Discord client ID not set in environment variables");
    options.ClientSecret = builder.Configuration["DiscordAuthentication:ClientSecret"] ?? throw new InvalidOperationException("Discord client secret not set in environment variables");
    options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;

    options.Scope.Add("identify");
    options.Scope.Add("guilds");

    options.SaveTokens = true;

    options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
    options.CorrelationCookie.SameSite = SameSiteMode.None;
});

#endregion

    var app = builder.Build();

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

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

internal class MissionCommandProcessor
{
}