using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using DcsMissionParser.Net;
using DcsOpsBoard.Database.Entities;
using DcsOpsBoard.Hubs.MissionSync;
using DcsOpsBoard.MissionEditing.RenderExtensions.Context;
using DcsOpsBoard.Services.MissionSync;
using OpenLayers.Blazor;

namespace DcsOpsBoard.MissionEditing;

public interface IMissionCommand
{
    Guid MissionId { get; }
    Guid MissionStateId { get; }
    
    public string CommandType { get; }
    ulong UserId { get; }
    DateTime Timestamp { get; }

    public Task<CommandResult> CheckPermissions(List<Permission> missionPermissions);
    public Task<CommandResult> ApplyToMission(DcsMission mission);
    public Task<CommandResult> RenderAsync(DcsRenderContext renderContext);
    
    private static JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
    public JsonElement ToJsonElement()
    {
        var json = JsonSerializer.Serialize(this, this.GetType(), _jsonOptions);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public static IMissionCommand? FromJsonElement(JsonElement payload)
    {
        try
        {
            Console.WriteLine($"Deserializing MissionCommand from JSON: {payload.GetRawText()}");
            
            // Extract CommandType from the JSON
            if (!payload.TryGetProperty(ToCamelCase(nameof(CommandType)), out JsonElement commandTypeElement))
            {
                Console.WriteLine("MissionCommand JSON does not contain CommandType property");
                return null;
            }

            string commandType = commandTypeElement.GetString() ?? string.Empty;
            
            if (string.IsNullOrEmpty(commandType))
            {
                Console.WriteLine("MissionCommand JSON CommandType is empty");
                return null;
            }

            // Look up the concrete type
            if (!_commandTypes.Value.TryGetValue(commandType, out Type? concreteType))
            {
                Console.WriteLine($"Unknown CommandType: {commandType}. Available types: {string.Join(", ", _commandTypes.Value.Keys)}");
                return null;
            }

            // Deserialize to the concrete type
            var command = JsonSerializer.Deserialize(payload.GetRawText(), concreteType, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return command as IMissionCommand;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to deserialize MissionCommand: {ex.Message}");
            Console.WriteLine($"Payload: {payload.GetRawText()}");
            return null;
        }
    }

    private static string ToCamelCase(string str) => char.ToLowerInvariant(str[0]) + str[1..];

    private static readonly Lazy<ConcurrentDictionary<string, Type>> _commandTypes = 
        new Lazy<ConcurrentDictionary<string, Type>>(DiscoverCommandTypes);

    private static ConcurrentDictionary<string, Type> DiscoverCommandTypes()
    {
        var types = new ConcurrentDictionary<string, Type>();
        
        // Find all types that implement IMissionCommand in the current assembly
        var commandTypes = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => typeof(IMissionCommand).IsAssignableFrom(t) 
                        && !t.IsInterface 
                        && !t.IsAbstract);

        foreach (var type in commandTypes)
        {
            // Use the type name as the key (matches CommandType => nameof(ClassName))
            types.TryAdd(type.Name, type);
        }

        return types;
    }

}
