using System;
using System.IO.Compression;
using DcsMissionParser.Net;
using DcsOpsBoard.Database.Configuration;
using DcsOpsBoard.Database.Context;
using DcsOpsBoard.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DcsOpsBoard.Database.Services;

/*
    Missions are stored on the disk, however, meta data are stored in the database. 
    The MissionStorageManager should abstract the logic of managing both away.

    It updates the mission (on disk) and in the database. 
    Performance buffering should be done in the cache layer above it. 

    Future additions: 
     - Script files management (add default scripts to the .miz file)
     - Intel images management (add default intel images to the .miz file)
     - Kneeboards (Maybe should be done by default anyway)
*/

/// <summary>
/// Manages the storage of missions on disk and their metadata in the database. <br>
/// </summary>
public interface IMissionStorageManager
{
    /// <summary>
    /// Creates a new mission from a .miz file. 
    /// </summary>
    /// <param name="uploadedMizFile">Mission file. Assumed to be validated</param>
    /// <param name="uploadedMissionName">Original name of the uploaded mission file</param>
    /// <param name="name">Name of the mission</param>
    /// <param name="description">Description of the mission</param>
    /// <param name="ownerId">ID of the user who owns the mission</param>
    /// <returns></returns>
    Task<OpsPlanningMission?> CreateNewMission(byte[] uploadedMizFile, string uploadedMissionName, string name, string description, ulong ownerId);

    /// <summary>
    /// Gets a mission by its ID. This will read the mission file from disk and parse it into a MizObject.
    /// </summary>
    /// <param name="missionId"></param>
    /// <exception cref="FileNotFoundException">Thrown when the mission file is not found on disk</exception>
    /// <returns>A DcsMission representing the mission</returns>
    Task<DcsMission?> GetMission(Guid missionId);

    /// <summary>
    /// Gets missions with optional filtering. This will read the mission files from disk and parse them into MizObjects.
    /// </summary>
    /// <param name="filter"></param>
    /// <returns></returns>
    Task<List<OpsPlanningMission>> GetMissions(Func<OpsPlanningMission, bool>? filter = null);

    /// <summary>
    /// Updates a mission. This will update the mission file on disk and update the metadata in the database. <br>
    /// Caching of changes is recommended and bulk updates are advised.
    /// </summary>
    /// <param name="missionId"></param>
    /// <param name="updatedMizObject"></param>
    /// <returns></returns>
    Task UpdateMission(Guid missionId, DcsMission updatedMizObject);

}

public class MissionStorageManager(IDbContextFactory<OpsBoardDbContext> _dbContextFactory, IOptions<DatabaseConfiguration> dbOptions) : IMissionStorageManager
{
    private readonly DatabaseConfiguration _config = dbOptions.Value;
    public async Task<OpsPlanningMission?> CreateNewMission(byte[] uploadedMizFile, string uploadedMissionName, string name, string description, ulong ownerId)
    {
        using OpsBoardDbContext dbContext = _dbContextFactory.CreateDbContext();
        OpsPlanningMission newMission = new()
        {
            Name = name,
            Description = description,
            UploadedMissionName = uploadedMissionName,
            OwnerId = ownerId
        };

        dbContext.OpsPlanningMissions.Add(newMission);
        dbContext.SaveChanges();

        string missionFilePath = GetMissionFilePath(newMission.MissionId);
        if(Directory.Exists(missionFilePath))
        {
            //Rather delete it and start fresh. This is a 1 in a billion billion chance (and really none with DB creating it).
            Directory.Delete(missionFilePath, true);
        }
        Directory.CreateDirectory(missionFilePath);

        using ZipArchive archive = new (new MemoryStream(uploadedMizFile), ZipArchiveMode.Read);
        await archive.ExtractToDirectoryAsync(GetMissionFilePath(newMission.MissionId));

        return newMission;
    }

    public async Task<DcsMission?> GetMission(Guid missionId)
    {
        string missionFilePath = GetMissionFilePath(missionId);
        if(!Directory.Exists(missionFilePath))        {
            throw new FileNotFoundException($"Mission file not found for mission ID {missionId}");
        }

        string missionFileFullPath = Path.Combine(missionFilePath, "mission");
        if(!File.Exists(missionFileFullPath))        {
            throw new FileNotFoundException($"Mission file not found for mission ID {missionId}");
        } 

        using Stream mizFileStream = File.OpenRead(missionFileFullPath);
        ParseResult<DcsMission> mizObject = await MissionSerializer.Deserialize(mizFileStream);
        if(!mizObject.Success || mizObject.Result == null)
        {
            throw new Exception($"Failed to parse mission file for mission ID {missionId}: {mizObject.FailureReason}");
        }
        return mizObject.Result;
    }

    public async Task<List<OpsPlanningMission>> GetMissions(Func<OpsPlanningMission, bool>? filter = null)
    {
        using OpsBoardDbContext dbContext = _dbContextFactory.CreateDbContext();
        if(filter == null)
        {
            return [..dbContext.OpsPlanningMissions];
        }
        else
        {
            return [.. dbContext.OpsPlanningMissions.Where(filter)];
        }
    }


    public async Task UpdateMission(Guid missionId, DcsMission updatedMizObject)
    {
        string missionFilePath = GetMissionFilePath(missionId);
        if(!Directory.Exists(missionFilePath))        {
            throw new FileNotFoundException($"Mission file not found for mission ID {missionId}");
        }
      
        string missionFileFullPath = Path.Combine(missionFilePath, "mission");
        ParseResult<byte[]> missionBytes = await MissionSerializer.Serialize(updatedMizObject);

        if(!missionBytes.Success || missionBytes.Result == null)
        {
            throw new Exception($"Failed to serialize mission object for mission ID {missionId}: {missionBytes.FailureReason}");
        }

        await File.WriteAllBytesAsync(missionFileFullPath, missionBytes.Result);

        using OpsBoardDbContext dbContext = _dbContextFactory.CreateDbContext();
        OpsPlanningMission? mission = await dbContext.OpsPlanningMissions.FirstOrDefaultAsync(m => m.MissionId == missionId) ?? throw new KeyNotFoundException($"Mission not found for mission ID in database for {missionId}");
    
        mission.LastEditedAt = DateTime.UtcNow;
        dbContext.OpsPlanningMissions.Update(mission);
        await dbContext.SaveChangesAsync();
    }

    private string GetMissionFilePath(Guid missionId)
    {
        return Path.Combine(_config.MissionFilesPath, missionId.ToString("N"));
    }
}
