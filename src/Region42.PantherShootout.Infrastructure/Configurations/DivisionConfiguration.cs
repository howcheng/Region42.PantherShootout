using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Region42.PantherShootout.Domain.Entities;

namespace Region42.PantherShootout.Infrastructure.Configurations;

public class DivisionConfiguration : IEntityTypeConfiguration<Division>
{
	public void Configure(EntityTypeBuilder<Division> builder)
	{
		builder.ToTable("divisions");

		builder.Property(d => d.Name).HasMaxLength(100).IsRequired();

		builder.HasIndex(d => new { d.TournamentYearId, d.Name }).IsUnique();

		builder.HasMany(d => d.Pools)
			.WithOne(p => p.Division)
			.HasForeignKey(p => p.DivisionId)
			.OnDelete(DeleteBehavior.Restrict);
	}
}
