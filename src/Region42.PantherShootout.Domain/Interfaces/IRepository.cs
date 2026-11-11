using System.Linq.Expressions;
using Region42.PantherShootout.Domain.Common;

namespace Region42.PantherShootout.Domain.Interfaces;

/// <summary>
/// Generic repository abstraction over a single entity type, backed by the scoped
/// <see cref="ITournamentDataContext"/>. Transaction control lives directly on the repository
/// via <see cref="BeginTransactionAsync"/> (no separate Unit-of-Work class).
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
public interface IRepository<T> where T : AuditableEntity
{
	Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

	Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default);

	Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

	Task AddAsync(T entity, CancellationToken cancellationToken = default);

	void Update(T entity);

	void Remove(T entity);

	Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

	Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
