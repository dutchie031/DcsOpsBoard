using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using DcsOpsBoard.Database.Services;

namespace DcsOpsBoard.Database;

public static class DatabaseExtensions
{
    /// <summary>
    /// Registers the database services and configurations. This should be called in the startup of the application. <br>
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    /// <exception cref="ArgumentException"></exception>
    public static void RegisterDatabase(this IServiceCollection services, IConfiguration configuration)
    {

        var databaseSection = configuration.GetSection("Database").Get<Configuration.DatabaseConfiguration>() ?? throw new ArgumentException("Database configuration section is missing");
        
        if(string.IsNullOrEmpty(databaseSection.Path))
            throw new ArgumentException("Database path is not configured");
        
        if(string.IsNullOrEmpty(databaseSection.MissionFilesPath))
            throw new ArgumentException("Mission files path is not configured");
        
        services.AddDbContextFactory<Context.OpsBoardDbContext>(options =>
            options.UseSqlite($"Data Source={databaseSection.Path}"));

        services.AddSingleton<IMissionStorageManager, MissionStorageManager>();
        services.AddSingleton<IPermissionManager, PermissionManager>();
    }
}
