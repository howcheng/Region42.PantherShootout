using Region42.PantherShootout.Domain.Documents;
using Region42.PantherShootout.Domain.Helpers;

namespace Region42.PantherShootout.Infrastructure.Storage;

internal static class StorageWriteLockHelper
{
	public static string GetLockObjectPath(string lockName, string competitionSlug = DivisionKeyHelper.DefaultCompetitionSlug)
	{
		return DivisionKeyHelper.GetLockObjectPath(lockName, competitionSlug);
	}

	public static bool IsExpired(StorageWriteLockDocument document, DateTime utcNow)
	{
		return document.ExpiresAtUtc <= utcNow;
	}

	public static StorageWriteLockDocument CreateDocument(string holder, TimeSpan ttl, DateTime utcNow)
	{
		return new StorageWriteLockDocument
		{
			Holder = holder,
			AcquiredAtUtc = utcNow,
			ExpiresAtUtc = utcNow.Add(ttl),
		};
	}
}
