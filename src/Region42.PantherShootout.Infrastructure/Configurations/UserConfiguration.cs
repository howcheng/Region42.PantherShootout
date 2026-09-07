using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Region42.PantherShootout.Domain.Entities;

namespace Region42.PantherShootout.Infrastructure.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
	public void Configure(EntityTypeBuilder<User> builder)
	{
		builder.ToTable("users");

		builder.Property(u => u.Email).HasMaxLength(320).IsRequired();
		builder.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();

		builder.HasIndex(u => u.Email).IsUnique();
	}
}
