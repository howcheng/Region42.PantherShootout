using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Region42.PantherShootout.Domain.Entities;

namespace Region42.PantherShootout.Infrastructure.Configurations;

public class PoolConfiguration : IEntityTypeConfiguration<Pool>
{
	public void Configure(EntityTypeBuilder<Pool> builder)
	{
		builder.ToTable("pools");

		builder.Property(p => p.Name).HasMaxLength(20).IsRequired();

		builder.HasIndex(p => new { p.DivisionId, p.Name }).IsUnique();

		builder.HasMany(p => p.Teams)
			.WithOne(t => t.Pool)
			.HasForeignKey(t => t.PoolId)
			.OnDelete(DeleteBehavior.Restrict);
	}
}
