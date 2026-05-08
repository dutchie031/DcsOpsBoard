using DcsOpsBoard.Components;
using DcsOpsBoard.Configuration;
using DcsOpsBoard.Services;

var builder = WebApplication.CreateBuilder(args);

var services = builder.Services;

services.AddScoped<IUserMapSettings, UserMapSettings>();

builder.Services.Configure<TileConfiguration>(builder.Configuration.GetSection("TileConfiguration"));

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllers();



var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapControllers();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
