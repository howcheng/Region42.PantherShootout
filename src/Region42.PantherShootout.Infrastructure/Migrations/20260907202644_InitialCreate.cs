using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Region42.PantherShootout.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "pso");

            migrationBuilder.CreateTable(
                name: "tournament_years",
                schema: "pso",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedByUserId = table.Column<int>(type: "integer", nullable: false),
                    RowVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tournament_years", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "pso",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedByUserId = table.Column<int>(type: "integer", nullable: false),
                    RowVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "divisions",
                schema: "pso",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TournamentYearId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Size = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedByUserId = table.Column<int>(type: "integer", nullable: false),
                    RowVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_divisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_divisions_tournament_years_TournamentYearId",
                        column: x => x.TournamentYearId,
                        principalSchema: "pso",
                        principalTable: "tournament_years",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pools",
                schema: "pso",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DivisionId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedByUserId = table.Column<int>(type: "integer", nullable: false),
                    RowVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pools", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pools_divisions_DivisionId",
                        column: x => x.DivisionId,
                        principalSchema: "pso",
                        principalTable: "divisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "teams",
                schema: "pso",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PoolId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedByUserId = table.Column<int>(type: "integer", nullable: false),
                    RowVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_teams_pools_PoolId",
                        column: x => x.PoolId,
                        principalSchema: "pso",
                        principalTable: "pools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "championship_games",
                schema: "pso",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DivisionId = table.Column<int>(type: "integer", nullable: false),
                    Slot = table.Column<int>(type: "integer", nullable: false),
                    HomeSourceDescription = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AwaySourceDescription = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    HomeTeamId = table.Column<int>(type: "integer", nullable: true),
                    AwayTeamId = table.Column<int>(type: "integer", nullable: true),
                    HomeGoals = table.Column<int>(type: "integer", nullable: true),
                    AwayGoals = table.Column<int>(type: "integer", nullable: true),
                    IsForfeit = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedByUserId = table.Column<int>(type: "integer", nullable: false),
                    RowVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_championship_games", x => x.Id);
                    table.ForeignKey(
                        name: "FK_championship_games_divisions_DivisionId",
                        column: x => x.DivisionId,
                        principalSchema: "pso",
                        principalTable: "divisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_championship_games_teams_AwayTeamId",
                        column: x => x.AwayTeamId,
                        principalSchema: "pso",
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_championship_games_teams_HomeTeamId",
                        column: x => x.HomeTeamId,
                        principalSchema: "pso",
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pool_play_games",
                schema: "pso",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PoolId = table.Column<int>(type: "integer", nullable: false),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    HomeTeamId = table.Column<int>(type: "integer", nullable: false),
                    AwayTeamId = table.Column<int>(type: "integer", nullable: false),
                    HomeGoals = table.Column<int>(type: "integer", nullable: true),
                    AwayGoals = table.Column<int>(type: "integer", nullable: true),
                    IsForfeit = table.Column<bool>(type: "boolean", nullable: false),
                    HomeYellowCards = table.Column<int>(type: "integer", nullable: false),
                    HomeRedCards = table.Column<int>(type: "integer", nullable: false),
                    AwayYellowCards = table.Column<int>(type: "integer", nullable: false),
                    AwayRedCards = table.Column<int>(type: "integer", nullable: false),
                    KftmWinningTeamId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedByUserId = table.Column<int>(type: "integer", nullable: false),
                    RowVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pool_play_games", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pool_play_games_pools_PoolId",
                        column: x => x.PoolId,
                        principalSchema: "pso",
                        principalTable: "pools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pool_play_games_teams_AwayTeamId",
                        column: x => x.AwayTeamId,
                        principalSchema: "pso",
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pool_play_games_teams_HomeTeamId",
                        column: x => x.HomeTeamId,
                        principalSchema: "pso",
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tiebreaker_overrides",
                schema: "pso",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DivisionId = table.Column<int>(type: "integer", nullable: false),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    TeamAId = table.Column<int>(type: "integer", nullable: false),
                    TeamBId = table.Column<int>(type: "integer", nullable: false),
                    WinningTeamId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedByUserId = table.Column<int>(type: "integer", nullable: false),
                    RowVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tiebreaker_overrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tiebreaker_overrides_divisions_DivisionId",
                        column: x => x.DivisionId,
                        principalSchema: "pso",
                        principalTable: "divisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tiebreaker_overrides_teams_TeamAId",
                        column: x => x.TeamAId,
                        principalSchema: "pso",
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tiebreaker_overrides_teams_TeamBId",
                        column: x => x.TeamBId,
                        principalSchema: "pso",
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tiebreaker_overrides_teams_WinningTeamId",
                        column: x => x.WinningTeamId,
                        principalSchema: "pso",
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shootout_rounds",
                schema: "pso",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PoolPlayGameId = table.Column<int>(type: "integer", nullable: false),
                    HomePkGoals = table.Column<int>(type: "integer", nullable: true),
                    AwayPkGoals = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedByUserId = table.Column<int>(type: "integer", nullable: false),
                    RowVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shootout_rounds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shootout_rounds_pool_play_games_PoolPlayGameId",
                        column: x => x.PoolPlayGameId,
                        principalSchema: "pso",
                        principalTable: "pool_play_games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_championship_games_AwayTeamId",
                schema: "pso",
                table: "championship_games",
                column: "AwayTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_championship_games_DivisionId_Slot",
                schema: "pso",
                table: "championship_games",
                columns: new[] { "DivisionId", "Slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_championship_games_HomeTeamId",
                schema: "pso",
                table: "championship_games",
                column: "HomeTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_divisions_TournamentYearId_Name",
                schema: "pso",
                table: "divisions",
                columns: new[] { "TournamentYearId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pool_play_games_AwayTeamId",
                schema: "pso",
                table: "pool_play_games",
                column: "AwayTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_pool_play_games_HomeTeamId",
                schema: "pso",
                table: "pool_play_games",
                column: "HomeTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_pool_play_games_PoolId_Round",
                schema: "pso",
                table: "pool_play_games",
                columns: new[] { "PoolId", "Round" });

            migrationBuilder.CreateIndex(
                name: "IX_pools_DivisionId_Name",
                schema: "pso",
                table: "pools",
                columns: new[] { "DivisionId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shootout_rounds_PoolPlayGameId",
                schema: "pso",
                table: "shootout_rounds",
                column: "PoolPlayGameId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_teams_PoolId_Name",
                schema: "pso",
                table: "teams",
                columns: new[] { "PoolId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tiebreaker_overrides_DivisionId_Scope_TeamAId_TeamBId",
                schema: "pso",
                table: "tiebreaker_overrides",
                columns: new[] { "DivisionId", "Scope", "TeamAId", "TeamBId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tiebreaker_overrides_TeamAId",
                schema: "pso",
                table: "tiebreaker_overrides",
                column: "TeamAId");

            migrationBuilder.CreateIndex(
                name: "IX_tiebreaker_overrides_TeamBId",
                schema: "pso",
                table: "tiebreaker_overrides",
                column: "TeamBId");

            migrationBuilder.CreateIndex(
                name: "IX_tiebreaker_overrides_WinningTeamId",
                schema: "pso",
                table: "tiebreaker_overrides",
                column: "WinningTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_tournament_years_Year",
                schema: "pso",
                table: "tournament_years",
                column: "Year",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                schema: "pso",
                table: "users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "championship_games",
                schema: "pso");

            migrationBuilder.DropTable(
                name: "shootout_rounds",
                schema: "pso");

            migrationBuilder.DropTable(
                name: "tiebreaker_overrides",
                schema: "pso");

            migrationBuilder.DropTable(
                name: "users",
                schema: "pso");

            migrationBuilder.DropTable(
                name: "pool_play_games",
                schema: "pso");

            migrationBuilder.DropTable(
                name: "teams",
                schema: "pso");

            migrationBuilder.DropTable(
                name: "pools",
                schema: "pso");

            migrationBuilder.DropTable(
                name: "divisions",
                schema: "pso");

            migrationBuilder.DropTable(
                name: "tournament_years",
                schema: "pso");
        }
    }
}
