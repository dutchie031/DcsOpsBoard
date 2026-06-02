using System;
using DcsMissionParser.Net;
using DcsMissionParser.Net.CoordMapping;
using DcsMissionParser.Net.Objects.Coalitions;
using DcsMissionParser.Net.Objects.Coalitions.Countries;
using DcsMissionParser.Net.Objects.Coalitions.Countries.Groups;
using DcsMissionParser.Net.Objects.Coalitions.Units.Plane;
using DcsMissionParser.Net.Objects.Commons;
using DcsOpsBoard.Database.Entities;
using DcsOpsBoard.MissionEditing.RenderExtensions.Context;
using DcsOpsBoard.MissionEditing.UpdateCommands.DTO;
using DcsOpsBoard.Services.MissionSync;
using DcsOpsBoard.Types;
using DcsOpsBoard.Types.Enums;
using OpenLayers.Blazor;

namespace DcsOpsBoard.MissionEditing.UpdateCommands;

public record AddFlight(
    Guid MissionId,
    Guid MissionStateId,
    ulong UserId, 
    DateTime Timestamp,
    AddFlightRequest Request) : IMissionCommand
{
    public string CommandType => nameof(AddFlight);

    public async Task<CommandResult> CheckPermissions(List<Permission> missionPermissions)
    {
        return CommandResult.Success();
    }

    public async Task<CommandResult> ApplyToMission(DcsMission mission)
    {
        Coalition? coalition = Request.Coalition switch
        {
            CoalitionSide.Blue => mission.Coalitions.Blue,
            CoalitionSide.Red => mission.Coalitions.Red,
            CoalitionSide.Neutral => mission.Coalitions.Neutrals,
            _ => null
        };

        int countryId = Request.Coalition switch
        {
            CoalitionSide.Blue => (int)CountryCode.CJTF_BLUE,
            CoalitionSide.Red => (int)CountryCode.CJTF_RED,
            _ => (int)CountryCode.UN_PEACEKEEPERS
        };

        if(coalition == null)
        {
            return CommandResult.Failure("Invalid coalition specified");
        }

        if(mission.IsGroupNameExists(Request.FlightName))
        {
            return CommandResult.Failure("A group with the same name already exists in the mission");
        }

        Country country = coalition.Countries.FirstOrDefault(x => x.Id == countryId);
        if(country == null)
        {
            country = new Country
            {
                Id = countryId,
                Name = ((CountryCode)countryId).ToString(),
                Planes = new Planes()
            };
            coalition.Countries.Add(country);
        }
        
        if(country.Planes.Groups.Any(x => x.GroupName == Request.FlightName))
        {
            return CommandResult.Failure("A flight with the same name already exists in the specified coalition");
        }

        if(mission.Theatre == null)
        {
            return CommandResult.Failure("Mission theatre is required to add a flight");
        }

        if(mission.Theatre.ToMap() == Types.Map.Unknown)
        {
            return CommandResult.Failure("Unsupported mission theatre");
        }

        DcsCoord coord = mission.Theatre.ToMap().CoordConverter.LLtoLO(Request.Position);
;
        PlaneGroup newGroup = new PlaneGroup
        {
            GroupName = Request.FlightName,
            IsDynamicSpawnTemplate = false,
            IsHidden = false,
            GroupId = mission.NextGroupId,
            Uncontrolled = false,
            Tasking = Request.Tasking,
            Modulation = Modulation.AM,
            RadioSet = false,
            StartTime = 0,
            Route = new(),
            Units = [
                new ()
                {
                    Type = Request.PlaneType,
                    X = coord.X,
                    Y = coord.Y,
                    Name = $"{Request.FlightName}-1",
                    UnitId = mission.NextUnitId,
                    Alt = 0,
                    AltType = AltType.RADIO,
                    Speed = 0,
                    Skill = Skill.Client
                }
            ]
        };

        country.Planes.Groups.Add(newGroup);
        return CommandResult.Success();
    }

    public async Task<CommandResult> RenderAsync(DcsRenderContext renderContext)
    {   
        //TODO: IMPLEMENT
        return CommandResult.Success();
    }
}
