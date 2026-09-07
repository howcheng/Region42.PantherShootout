using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Region42.PantherShootout.Domain.Entities;

namespace Region42.PantherShootout.Infrastructure.Configurations;

public class TournamentYearConfiguration : IEntityTypeConfiguration<TournamentYear>
{
	public void Configure(EntityTypeBuilder<TournamentYear> builder)
	{
		builder.ToTable("tournament_years");

		builder.HasIndex(y => y.Year).IsUnique();

		builder.HasMany(y => y.Divisions)
			.WithOne(d => d.TournamentYear)
			.HasForeignKey(d => d.TournamentYearId)
			.OnDelete(DeleteBehavior.Restrict);
	}
}
