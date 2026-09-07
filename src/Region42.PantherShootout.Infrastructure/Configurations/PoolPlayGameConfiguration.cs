using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Region42.PantherShootout.Domain.Entities;

namespace Region42.PantherShootout.Infrastructure.Configurations;

public class PoolPlayGameConfiguration : IEntityTypeConfiguration<PoolPlayGame>
{
	public void Configure(EntityTypeBuilder<PoolPlayGame> builder)
	{
		builder.ToTable("pool_play_games");

		builder.HasIndex(g => new { g.PoolId, g.Round });

		builder.HasOne(g => g.Pool)
			.WithMany()
			.HasForeignKey(g => g.PoolId)
			.OnDelete(DeleteBehavior.Restrict);

		builder.HasOne(g => g.HomeTeam)
			.WithMany()
			.HasForeignKey(g => g.HomeTeamId)
			.OnDelete(DeleteBehavior.Restrict);

		builder.HasOne(g => g.AwayTeam)
			.WithMany()
			.HasForeignKey(g => g.AwayTeamId)
			.OnDelete(DeleteBehavior.Restrict);

		builder.HasOne(g => g.ShootoutRound)
			.WithOne(s => s.PoolPlayGame)
			.HasForeignKey<ShootoutRound>(s => s.PoolPlayGameId)
			.OnDelete(DeleteBehavior.Cascade);
	}
}
