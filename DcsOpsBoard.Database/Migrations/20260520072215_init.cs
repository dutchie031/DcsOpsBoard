using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DcsOpsBoard.Database.Migrations
{
    /// <inheritdoc />
    public partial class init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OpsPlanningMissions",
                columns: table => new
                {
                    MissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    UploadedMissionName = table.Column<string>(type: "TEXT", nullable: false),
                    Map = table.Column<int>(type: "INTEGER", nullable: false),
                    MissionType = table.Column<int>(type: "INTEGER", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastEditedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpsPlanningMissions", x => x.MissionId);
                });

            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    UserId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    MissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => new { x.UserId, x.MissionId, x.Role });
                    table.ForeignKey(
                        name: "FK_Permissions_OpsPlanningMissions_MissionId",
                        column: x => x.MissionId,
                        principalTable: "OpsPlanningMissions",
                        principalColumn: "MissionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserFlights",
                columns: table => new
                {
                    FlightId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FlightLead = table.Column<ulong>(type: "INTEGER", nullable: false),
                    FlightName = table.Column<string>(type: "TEXT", nullable: false),
                    MissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OpsPlanningMissionMissionId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserFlights", x => x.FlightId);
                    table.ForeignKey(
                        name: "FK_UserFlights_OpsPlanningMissions_MissionId",
                        column: x => x.MissionId,
                        principalTable: "OpsPlanningMissions",
                        principalColumn: "MissionId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserFlights_OpsPlanningMissions_OpsPlanningMissionMissionId",
                        column: x => x.OpsPlanningMissionMissionId,
                        principalTable: "OpsPlanningMissions",
                        principalColumn: "MissionId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_MissionId",
                table: "Permissions",
                column: "MissionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserFlights_MissionId",
                table: "UserFlights",
                column: "MissionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserFlights_OpsPlanningMissionMissionId",
                table: "UserFlights",
                column: "OpsPlanningMissionMissionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Permissions");

            migrationBuilder.DropTable(
                name: "UserFlights");

            migrationBuilder.DropTable(
                name: "OpsPlanningMissions");
        }
    }
}
