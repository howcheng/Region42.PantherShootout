using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Domain.Common;

namespace Region42.PantherShootout.Infrastructure;

/// <summary>
/// Stamps CreatedAt/CreatedByUserId on insert and ModifiedAt/ModifiedByUserId on every save,
/// and manually increments the simple integer <see cref="AuditableEntity.RowVersion"/> optimistic
/// concurrency token on every save of a changed entity.
/// </summary>
public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
	private readonly ICurrentUserService _currentUserService;

	public AuditSaveChangesInterceptor(ICurrentUserService currentUserService)
	{
		_currentUserService = currentUserService;
	}

	public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
	{
		ApplyAuditInfo(eventData.Context);
		return base.SavingChanges(eventData, result);
	}

	public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
		DbContextEventData eventData,
		InterceptionResult<int> result,
		CancellationToken cancellationToken = default)
	{
		ApplyAuditInfo(eventData.Context);
		return base.SavingChangesAsync(eventData, result, cancellationToken);
	}

	private void ApplyAuditInfo(DbContext? context)
	{
		if (context is null)
		{
			return;
		}

		var now = DateTimeOffset.UtcNow;
		var userId = _currentUserService.UserId;

		foreach (EntityEntry<AuditableEntity> entry in context.ChangeTracker.Entries<AuditableEntity>())
		{
			switch (entry.State)
			{
				case EntityState.Added:
					entry.Entity.CreatedAt = now;
					entry.Entity.CreatedByUserId = userId;
					entry.Entity.ModifiedAt = now;
					entry.Entity.ModifiedByUserId = userId;
					entry.Entity.RowVersion = 1;
					break;
				case EntityState.Modified:
					entry.Entity.ModifiedAt = now;
					entry.Entity.ModifiedByUserId = userId;
					entry.Entity.RowVersion += 1;
					break;
			}
		}
	}
}
