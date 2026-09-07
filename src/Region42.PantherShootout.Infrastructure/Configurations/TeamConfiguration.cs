using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Region42.PantherShootout.Domain.Entities;

namespace Region42.PantherShootout.Infrastructure.Configurations;

public class TeamConfiguration : IEntityTypeConfiguration<Team>
{
	public void Configure(EntityTypeBuilder<Team> builder)
	{
		builder.ToTable("teams");

		builder.Property(t => t.Name).HasMaxLength(150).IsRequired();

		builder.HasIndex(t => new { t.PoolId, t.Name }).IsUnique();
	}
}
