using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Region42.PantherShootout.Domain.Entities;

namespace Region42.PantherShootout.Infrastructure.Configurations;

public class ShootoutRoundConfiguration : IEntityTypeConfiguration<ShootoutRound>
{
	public void Configure(EntityTypeBuilder<ShootoutRound> builder)
	{
		builder.ToTable("shootout_rounds");

		builder.HasIndex(s => s.PoolPlayGameId).IsUnique();
	}
}
