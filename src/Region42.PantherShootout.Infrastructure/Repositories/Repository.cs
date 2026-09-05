using Microsoft.EntityFrameworkCore;

namespace Region42.PantherShootout.Infrastructure.Repositories;

/// <summary>
/// Default EF Core-backed implementation of <see cref="IRepository{T}"/>, registered as an open
/// generic in DI (see <c>Region42.PantherShootout.Infrastructure.ServiceCollectionExtensions</c>).
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
public class Repository<T> : IRepository<T> where T : class
{
	private readonly DbContext _dbContext;
	private readonly DbSet<T> _dbSet;

	public Repository(DbContext dbContext)
	{
		_dbContext = dbContext;
		_dbSet = dbContext.Set<T>();
	}

	public IQueryable<T> Query() => _dbSet.AsQueryable();

	public async Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
		=> await _dbSet.FindAsync([id], cancellationToken);

	public async Task AddAsync(T entity, CancellationToken cancellationToken = default)
		=> await _dbSet.AddAsync(entity, cancellationToken);

	public void Update(T entity) => _dbSet.Update(entity);

	public void Remove(T entity) => _dbSet.Remove(entity);

	public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
		=> _dbContext.SaveChangesAsync(cancellationToken);
}
