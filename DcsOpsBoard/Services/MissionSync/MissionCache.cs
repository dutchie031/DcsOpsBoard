using System;
using DcsMissionParser.Net;
using DcsMissionParser.Net.Objects.Coalitions.Countries.Groups;
using DcsOpsBoard.Database.Services;
using DcsOpsBoard.Types.Enums;

namespace DcsOpsBoard.Hubs.MissionSync;


public interface IMissionCache
{
    Task<DcsMission?> GetMission(Guid missionId);
    Task<CoalitionSide> GetCoalitionForGroup(Guid missionId, Guid groupId);
    Task<CoalitionSide> GetCoalitionForUnit(Guid missionId, Guid groupId);
    
    /// <summary>
    /// Apply an update action to a mission. This will load the mission into the cache if it's not already loaded, apply the update, and mark the mission as dirty so it will be persisted to storage on the next persist cycle.
    /// </summary>
    /// <param name="missionId">The ID of the mission to update.</param>
    /// <param name="updateAction">The action to apply to the mission.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the mission is not in sync.</exception>
    /// <exception cref="KeyNotFoundException">Thrown if the mission is not found.</exception>
    Task UpdateMission(Guid missionId, Guid missionStateId, Action<DcsMission> updateAction);

    Task PersistMissions();
    Task EvictOldMissions(TimeSpan maxAge);
}

public class MissionCache(IMissionStorageManager _missionStorageManager) : BackgroundService, IMissionCache
{
    private readonly Dictionary<Guid, DcsMission> _missionCache = [];
    private readonly Dictionary<Guid, CoalitionCache> _coalitionCache = [];
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
        if (!_missionCache.TryGetValue(missionId, out DcsMission? mission))
        {
            mission = await _missionStorageManager.GetMission(missionId);
            if (mission != null)
            {
                _missionCache[missionId] = mission;
                _coalitionCache[missionId] = new CoalitionCache(mission);
            }
        }
        
        return mission;
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

    public async Task UpdateMission(Guid missionId, Guid missionStateId, Action<DcsMission> updateAction)
    {
        DcsMission? mission = await GetMission(missionId) ?? throw new KeyNotFoundException("Mission not found");
        if (mission.ParserId != missionStateId)
        {
            throw new InvalidOperationException("Mission state is out of date");
        }
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

    public async Task<CoalitionSide> GetCoalitionForGroup(Guid missionId,Guid groupId)
    {
        if(!_coalitionCache.TryGetValue(groupId, out CoalitionCache? coalitionCache))
        {
            DcsMission mission = await GetMission(missionId) ?? throw new InvalidOperationException("Mission not found");
            _coalitionCache[groupId] = new CoalitionCache(mission);
            coalitionCache = _coalitionCache[groupId];
        }

        if(coalitionCache.CoalitionPerId.TryGetValue(groupId, out CoalitionSide coalition))
        {
            return coalition;
        }
        else
        {
            DcsMission mission = await GetMission(missionId) ?? throw new InvalidOperationException("Mission not found");
            foreach(PlaneGroup group in mission.Coalitions.Blue?.Countries.SelectMany(c => c.Planes.Groups) ?? [])
            {
                coalitionCache.CoalitionPerId[groupId] = CoalitionSide.Blue;
                foreach(var unit in group.Units)
                {
                    coalitionCache.CoalitionPerId[unit.RefId] = CoalitionSide.Blue;
                    return CoalitionSide.Blue;
                }
                return CoalitionSide.Blue;
            }

            foreach(PlaneGroup group in mission.Coalitions.Red?.Countries.SelectMany(c => c.Planes.Groups) ?? [])
            {
                coalitionCache.CoalitionPerId[groupId] = CoalitionSide.Red;
                foreach(var unit in group.Units)
                {
                    coalitionCache.CoalitionPerId[unit.RefId] = CoalitionSide.Red;
                    return CoalitionSide.Red;
                }
                return CoalitionSide.Red;
            }

            foreach(PlaneGroup group in mission.Coalitions.Neutrals?.Countries.SelectMany(c => c.Planes.Groups) ?? [])
            {
                coalitionCache.CoalitionPerId[groupId] = CoalitionSide.Neutral;
                foreach(var unit in group.Units)
                {
                    coalitionCache.CoalitionPerId[unit.RefId] = CoalitionSide.Neutral;
                    return CoalitionSide.Neutral;
                }
                return CoalitionSide.Neutral;
            }
        }
        return CoalitionSide.Unknown;
    }

    public async Task<CoalitionSide> GetCoalitionForUnit(Guid missionId,Guid groupId)
    {
        if(!_coalitionCache.TryGetValue(groupId, out CoalitionCache? coalitionCache))
        {
            DcsMission mission = await GetMission(missionId) ?? throw new InvalidOperationException("Mission not found");
            _coalitionCache[groupId] = new CoalitionCache(mission);
            coalitionCache = _coalitionCache[groupId];
        }

        if(coalitionCache.CoalitionPerId.TryGetValue(groupId, out CoalitionSide coalition))
        {
            return coalition;
        }
        else
        {
            DcsMission mission = await GetMission(missionId) ?? throw new InvalidOperationException("Mission not found");
            foreach(PlaneGroup group in mission.Coalitions.Blue?.Countries.SelectMany(c => c.Planes.Groups) ?? [])
            {
                if(group.RefId == groupId)
                {
                    coalitionCache.CoalitionPerId[groupId] = CoalitionSide.Blue;
                    foreach(var unit in group.Units)
                    {
                        coalitionCache.CoalitionPerId[unit.RefId] = CoalitionSide.Blue;
                    }
                    return CoalitionSide.Blue;
                }
            }

            foreach(PlaneGroup group in mission.Coalitions.Red?.Countries.SelectMany(c => c.Planes.Groups) ?? [])
            {
                if(group.RefId == groupId)
                {
                    coalitionCache.CoalitionPerId[groupId] = CoalitionSide.Red;
                    foreach(var unit in group.Units)
                    {
                        coalitionCache.CoalitionPerId[unit.RefId] = CoalitionSide.Red;
                    }
                    return CoalitionSide.Red;
                }
            }

            foreach(PlaneGroup group in mission.Coalitions.Neutrals?.Countries.SelectMany(c => c.Planes.Groups) ?? [])
            {
                if(group.RefId == groupId)
                {
                    coalitionCache.CoalitionPerId[groupId] = CoalitionSide.Neutral;
                    foreach(var unit in group.Units)
                    {
                        coalitionCache.CoalitionPerId[unit.RefId] = CoalitionSide.Neutral;
                    }
                    return CoalitionSide.Neutral;
                }
            }
        }
        return CoalitionSide.Unknown;        
    }


    private class CoalitionCache
    {
        public Dictionary<Guid, CoalitionSide> CoalitionPerId { get; } = [];


        public CoalitionCache(DcsMission mission)
        {
            foreach(PlaneGroup group in mission.Coalitions.Blue?.Countries.SelectMany(c => c.Planes.Groups) ?? [])
            {
                CoalitionPerId[group.RefId] = CoalitionSide.Blue;
                foreach(var unit in group.Units)
                {
                    CoalitionPerId[unit.RefId] = CoalitionSide.Blue;
                }
            }

            foreach(PlaneGroup group in mission.Coalitions.Red?.Countries.SelectMany(c => c.Planes.Groups) ?? [])
            {
                CoalitionPerId[group.RefId] = CoalitionSide.Red;
                foreach(var unit in group.Units)
                {
                    CoalitionPerId[unit.RefId] = CoalitionSide.Red;
                }
            }

            foreach(PlaneGroup group in mission.Coalitions.Neutrals?.Countries.SelectMany(c => c.Planes.Groups) ?? [])
            {
                CoalitionPerId[group.RefId] = CoalitionSide.Neutral;
                foreach(var unit in group.Units)
                {
                    CoalitionPerId[unit.RefId] = CoalitionSide.Neutral;
                }
            }
        }
    }

}
