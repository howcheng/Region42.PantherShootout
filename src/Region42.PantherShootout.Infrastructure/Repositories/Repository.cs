using System.Linq.Expressions;
using Region42.PantherShootout.Domain.Common;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Interfaces;

namespace Region42.PantherShootout.Infrastructure.Repositories;

/// <summary>
/// Generic in-memory CRUD repository over <see cref="ITournamentDataContext"/>.
/// </summary>
/// <typeparam name="T">The domain entity type.</typeparam>
public class Repository<T> : IRepository<T> where T : AuditableEntity
{
	private readonly ITournamentDataContext _context;

	public Repository(ITournamentDataContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		_context = context;
	}

	public async Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
	{
		await _context.LoadAllAsync(cancellationToken);
		return GetEntities().FirstOrDefault(e => e.Id == id);
	}

	private IEnumerable<T> GetEntities()
	{
		if (typeof(T) == typeof(TournamentYear))
		{
			return (IEnumerable<T>)_context.TournamentYears;
		}

		if (typeof(T) == typeof(Division))
		{
			return (IEnumerable<T>)_context.Divisions;
		}

		if (typeof(T) == typeof(Pool))
		{
			return (IEnumerable<T>)_context.Pools;
		}

		if (typeof(T) == typeof(Team))
		{
			return (IEnumerable<T>)_context.Teams;
		}

		if (typeof(T) == typeof(PoolPlayGame))
		{
			return (IEnumerable<T>)_context.PoolPlayGames;
		}

		if (typeof(T) == typeof(ChampionshipGame))
		{
			return (IEnumerable<T>)_context.ChampionshipGames;
		}

		if (typeof(T) == typeof(ShootoutRound))
		{
			return (IEnumerable<T>)_context.ShootoutRounds;
		}

		if (typeof(T) == typeof(TiebreakerOverride))
		{
			return (IEnumerable<T>)_context.TiebreakerOverrides;
		}

		if (typeof(T) == typeof(AysoRegion))
		{
			return (IEnumerable<T>)_context.Regions;
		}

		throw new NotSupportedException($"Repository for entity type '{typeof(T).Name}' is not supported.");
	}

	public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default)
	{
		await _context.LoadAllAsync(cancellationToken);
		return GetEntities().ToList();
	}

	public Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(predicate);

		return FindInternalAsync(predicate, cancellationToken);
	}

	private async Task<IReadOnlyList<T>> FindInternalAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken)
	{
		await _context.LoadAllAsync(cancellationToken);
		var compiled = predicate.Compile();
		return GetEntities().Where(compiled).ToList();
	}

	public Task AddAsync(T entity, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entity);

		return AddInternalAsync(entity, cancellationToken);
	}

	private async Task AddInternalAsync(T entity, CancellationToken cancellationToken)
	{
		await _context.LoadAllAsync(cancellationToken);
		_context.Add(entity);
	}

	public void Update(T entity)
	{
		ArgumentNullException.ThrowIfNull(entity);

		_context.Update(entity);
	}

	public void Remove(T entity)
	{
		ArgumentNullException.ThrowIfNull(entity);

		_context.Remove(entity);
	}

	public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
	{
		return _context.SaveChangesAsync(cancellationToken);
	}

	public async Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
	{
		await _context.LoadAllAsync(cancellationToken);
		return await _context.BeginTransactionAsync(cancellationToken);
	}
}
