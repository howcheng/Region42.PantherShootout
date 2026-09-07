using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Region42.PantherShootout.Domain.Entities;

namespace Region42.PantherShootout.Infrastructure.Configurations;

public class TiebreakerOverrideConfiguration : IEntityTypeConfiguration<TiebreakerOverride>
{
	public void Configure(EntityTypeBuilder<TiebreakerOverride> builder)
	{
		builder.ToTable("tiebreaker_overrides");

		builder.HasIndex(o => new { o.DivisionId, o.Scope, o.TeamAId, o.TeamBId }).IsUnique();

		builder.HasOne(o => o.Division)
			.WithMany()
			.HasForeignKey(o => o.DivisionId)
			.OnDelete(DeleteBehavior.Restrict);

		builder.HasOne(o => o.TeamA)
			.WithMany()
			.HasForeignKey(o => o.TeamAId)
			.OnDelete(DeleteBehavior.Restrict);

		builder.HasOne(o => o.TeamB)
			.WithMany()
			.HasForeignKey(o => o.TeamBId)
			.OnDelete(DeleteBehavior.Restrict);

		builder.HasOne(o => o.WinningTeam)
			.WithMany()
			.HasForeignKey(o => o.WinningTeamId)
			.OnDelete(DeleteBehavior.Restrict);
	}
}
