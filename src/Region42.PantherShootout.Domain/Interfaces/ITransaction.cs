namespace Region42.PantherShootout.Domain.Interfaces;

/// <summary>
/// Represents an in-memory transaction over the scoped tournament data context that can be
/// committed or rolled back across multiple repository operations before flushing to JSON storage.
/// </summary>
public interface ITransaction : IAsyncDisposable
{
	Task CommitAsync(CancellationToken cancellationToken = default);

	Task RollbackAsync(CancellationToken cancellationToken = default);
}
