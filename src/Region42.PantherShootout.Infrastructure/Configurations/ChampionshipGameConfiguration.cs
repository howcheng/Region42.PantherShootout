using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Region42.PantherShootout.Domain.Entities;

namespace Region42.PantherShootout.Infrastructure.Configurations;

public class ChampionshipGameConfiguration : IEntityTypeConfiguration<ChampionshipGame>
{
	public void Configure(EntityTypeBuilder<ChampionshipGame> builder)
	{
		builder.ToTable("championship_games");

		builder.Property(g => g.HomeSourceDescription).HasMaxLength(100).IsRequired();
		builder.Property(g => g.AwaySourceDescription).HasMaxLength(100).IsRequired();

		builder.HasIndex(g => new { g.DivisionId, g.Slot }).IsUnique();

		builder.HasOne(g => g.Division)
			.WithMany()
			.HasForeignKey(g => g.DivisionId)
			.OnDelete(DeleteBehavior.Restrict);

		builder.HasOne(g => g.HomeTeam)
			.WithMany()
			.HasForeignKey(g => g.HomeTeamId)
			.OnDelete(DeleteBehavior.Restrict);

		builder.HasOne(g => g.AwayTeam)
			.WithMany()
			.HasForeignKey(g => g.AwayTeamId)
			.OnDelete(DeleteBehavior.Restrict);
	}
}
