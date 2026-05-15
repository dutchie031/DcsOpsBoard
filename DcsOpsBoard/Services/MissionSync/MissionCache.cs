using System;
using DcsMissionParser.Net;
using DcsOpsBoard.Database.Services;

namespace DcsOpsBoard.Hubs.MissionSync;


public interface IMissionCache
{
    Task<DcsMission?> GetMission(Guid missionId);
    Task UpdateMission(Guid missionId, Action<DcsMission> updateAction);

    Task PersistMissions();
    Task EvictOldMissions(TimeSpan maxAge);
}

public class MissionCache(IMissionStorageManager _missionStorageManager) : BackgroundService, IMissionCache
{
    private readonly Dictionary<Guid, DcsMission> _missionCache = [];
    private readonly Dictionary<Guid, DateTime> _lastAccessed = [];
    private readonly Dictionary<Guid, bool> _dirtyFlags = [];

    public Task EvictOldMissions(TimeSpan maxAge)
    {
        foreach (var kvp in _lastAccessed)
        {
            Guid missionId = kvp.Key;
            DateTime lastAccess = kvp.Value;

            if (DateTime.UtcNow - lastAccess > maxAge)
            {
                _missionCache.Remove(missionId);
                _lastAccessed.Remove(missionId);
                _dirtyFlags.Remove(missionId);
            }
        }
        return Task.CompletedTask;
    }

    public async Task<DcsMission?> GetMission(Guid missionId)
    {
        _lastAccessed[missionId] = DateTime.UtcNow;
        if (_missionCache.TryGetValue(missionId, out DcsMission? mission))
        {
            return mission;
        }
        
        return await _missionStorageManager.GetMission(missionId);
    }

    public async Task PersistMissions()
    {
        foreach (var kvp in _missionCache)
        {
            Guid missionId = kvp.Key;
            DcsMission mission = kvp.Value;

            if (_dirtyFlags.TryGetValue(missionId, out bool isDirty) && isDirty)
            {
                await _missionStorageManager.UpdateMission(missionId, mission);
                _dirtyFlags[missionId] = false;
            }
        }
    }

    public async Task UpdateMission(Guid missionId, Action<DcsMission> updateAction)
    {
        DcsMission? mission = await GetMission(missionId) ?? throw new InvalidOperationException("Mission not found");
        updateAction(mission);
        _dirtyFlags[missionId] = true;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await PersistMissions();
        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PersistMissions();
                await EvictOldMissions(TimeSpan.FromMinutes(10));
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Expected on shutdown, ignore
            }
            catch (Exception ex)
            {
                // Log error but continue loop
                Console.Error.WriteLine($"Error in persist loop: {ex}");
            }
        }
    }

}
