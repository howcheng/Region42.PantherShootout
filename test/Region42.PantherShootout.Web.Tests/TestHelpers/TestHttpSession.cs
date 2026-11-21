using Microsoft.AspNetCore.Http;

namespace Region42.PantherShootout.Web.Tests.TestHelpers;

internal sealed class TestHttpSession : ISession
{
	private readonly Dictionary<string, byte[]> _store = new(StringComparer.Ordinal);

	public string Id { get; } = Guid.NewGuid().ToString("N");

	public bool IsAvailable => true;

	public IEnumerable<string> Keys => _store.Keys;

	public void Clear() => _store.Clear();

	public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	public void Remove(string key) => _store.Remove(key);

	public void Set(string key, byte[] value) => _store[key] = value;

	public bool TryGetValue(string key, out byte[] value)
	{
		if (_store.TryGetValue(key, out var found))
		{
			value = found;
			return true;
		}

		value = Array.Empty<byte>();
		return false;
	}
}
