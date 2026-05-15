using System;
using Microsoft.EntityFrameworkCore;

namespace DcsOpsBoard.Database.Context;

public class OpsBoardDbContext : DbContext
{
    public DbSet<Entities.OpsPlanningMission> OpsPlanningMissions { get; set; }
    public DbSet<Entities.Permission> Permissions { get; set; }

    public OpsBoardDbContext(DbContextOptions<OpsBoardDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Entities.Permission>()
            .HasKey(p => new { p.UserId, p.MissionId, p.Role });

        modelBuilder.Entity<Entities.OpsPlanningMission>()
            .HasMany(m => m.Permissions)
            .WithOne()
            .HasForeignKey(p => p.MissionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Entities.UserFlights>()
            .HasKey(uf => uf.FlightId);

        modelBuilder.Entity<Entities.UserFlights>()
            .HasOne<Entities.OpsPlanningMission>()
            .WithMany()
            .HasForeignKey(uf => uf.MissionId)
            .OnDelete(DeleteBehavior.Cascade);

    }
    
}
