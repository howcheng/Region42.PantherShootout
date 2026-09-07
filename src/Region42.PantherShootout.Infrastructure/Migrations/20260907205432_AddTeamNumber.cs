using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Region42.PantherShootout.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TeamNumber",
                schema: "pso",
                table: "teams",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_teams_PoolId_TeamNumber",
                schema: "pso",
                table: "teams",
                columns: new[] { "PoolId", "TeamNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_teams_PoolId_TeamNumber",
                schema: "pso",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "TeamNumber",
                schema: "pso",
                table: "teams");
        }
    }
}
