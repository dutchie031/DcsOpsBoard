using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DcsOpsBoard.Database.Entities;

public class UserFlights
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid FlightId { get; set; }

    public required ulong FlightLead { get; set; }

    public required string FlightName { get; set; }
    
    public required Guid MissionId { get; set; }
}
