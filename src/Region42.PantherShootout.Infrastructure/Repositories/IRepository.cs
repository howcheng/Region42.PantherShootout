namespace Region42.PantherShootout.Infrastructure.Repositories;

/// <summary>
/// Generic repository abstraction over a single entity type, used instead of exposing the raw
/// EF Core <c>DbContext</c> to the Application layer so services are easy to unit test with mocks.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
public interface IRepository<T> where T : class
{
	IQueryable<T> Query();

	Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

	Task AddAsync(T entity, CancellationToken cancellationToken = default);

	void Update(T entity);

	void Remove(T entity);

	Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
