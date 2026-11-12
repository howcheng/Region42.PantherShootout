using Region42.PantherShootout.Domain.Documents;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;

namespace Region42.PantherShootout.Domain.Helpers;

/// <summary>
/// Maps between persisted JSON document models and in-memory domain entities.
/// </summary>
public static class DocumentEntityMapper
{
	public static TournamentYear ToEntity(TournamentInfoDocument doc)
	{
		ArgumentNullException.ThrowIfNull(doc);

		return new TournamentYear
		{
			Id = doc.Id,
			Year = doc.Year,
			Name = doc.Name,
			IsActive = doc.IsActive,
			CustomMessage = doc.CustomMessage,
		};
	}

	public static TournamentInfoDocument ToDocument(TournamentYear entity)
	{
		ArgumentNullException.ThrowIfNull(entity);

		return new TournamentInfoDocument
		{
			Id = entity.Id,
			Year = entity.Year,
			Name = entity.Name,
			IsActive = entity.IsActive,
			CustomMessage = entity.CustomMessage,
		};
	}

	public static Division ToEntity(DivisionDocument doc, int tournamentYearId, TournamentYear? tournamentYear = null)
	{
		ArgumentNullException.ThrowIfNull(doc);

		var ageGroup = DivisionKeyHelper.ParseAgeGroup(doc.AgeGroup);
		var gender = DivisionKeyHelper.ParseGender(doc.Gender);
		var displayName = string.IsNullOrWhiteSpace(doc.Name)
			? DivisionKeyHelper.ToDisplayName(ageGroup, gender)
			: doc.Name;

		return new Division
		{
			Id = doc.Id,
			TournamentYearId = tournamentYearId,
			TournamentYear = tournamentYear,
			AgeGroup = ageGroup,
			Gender = gender,
			Name = displayName,
			Size = (DivisionSize)doc.Size,
			CustomMessage = doc.CustomMessage,
		};
	}

	public static DivisionDocument ToDocument(
		Division entity,
		IEnumerable<Pool> pools,
		IEnumerable<Team> teams)
	{
		ArgumentNullException.ThrowIfNull(entity);
		ArgumentNullException.ThrowIfNull(pools);
		ArgumentNullException.ThrowIfNull(teams);

		var teamsByPoolId = teams
			.GroupBy(t => t.PoolId)
			.ToDictionary(g => g.Key, g => g.OrderBy(t => t.TeamNumber).ThenBy(t => t.Id).ToList());

		return new DivisionDocument
		{
			Id = entity.Id,
			Key = DivisionKeyHelper.ToKey(entity.AgeGroup, entity.Gender),
			Name = entity.Name,
			AgeGroup = DivisionKeyHelper.ToAgeGroupString(entity.AgeGroup),
			Gender = DivisionKeyHelper.ToGenderString(entity.Gender),
			Size = (int)entity.Size,
			CustomMessage = entity.CustomMessage,
			Pools = pools
				.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
				.Select(p => ToDocument(p, teamsByPoolId.GetValueOrDefault(p.Id) ?? []))
				.ToList(),
		};
	}

	public static Pool ToEntity(PoolDocument doc, int divisionId, Division? division = null)
	{
		ArgumentNullException.ThrowIfNull(doc);

		return new Pool
		{
			Id = doc.Id,
			DivisionId = divisionId,
			Division = division,
			Name = doc.Name,
		};
	}

	public static PoolDocument ToDocument(Pool entity, IEnumerable<Team> teams)
	{
		ArgumentNullException.ThrowIfNull(entity);
		ArgumentNullException.ThrowIfNull(teams);

		return new PoolDocument
		{
			Id = entity.Id,
			Name = entity.Name,
			Teams = teams
				.OrderBy(t => t.TeamNumber)
				.ThenBy(t => t.Id)
				.Select(ToDocument)
				.ToList(),
		};
	}

	public static Team ToEntity(TeamDocument doc, int poolId, Pool? pool = null)
	{
		ArgumentNullException.ThrowIfNull(doc);

		return new Team
		{
			Id = doc.Id,
			PoolId = poolId,
			Pool = pool,
			TeamNumber = doc.TeamNumber,
			RegionNumber = doc.RegionNumber,
			CoachName = doc.CoachName ?? string.Empty,
			Name = doc.Name,
		};
	}

	public static TeamDocument ToDocument(Team entity)
	{
		ArgumentNullException.ThrowIfNull(entity);

		return new TeamDocument
		{
			Id = entity.Id,
			TeamNumber = entity.TeamNumber,
			RegionNumber = entity.RegionNumber,
			CoachName = string.IsNullOrWhiteSpace(entity.CoachName) ? null : entity.CoachName,
			Name = entity.Name,
		};
	}

	public static AysoRegion ToEntity(RegionItemDocument doc)
	{
		ArgumentNullException.ThrowIfNull(doc);

		return new AysoRegion
		{
			Id = doc.Id,
			RegionNumber = doc.RegionNumber,
			Name = doc.Name,
		};
	}

	public static RegionItemDocument ToDocument(AysoRegion entity)
	{
		ArgumentNullException.ThrowIfNull(entity);

		return new RegionItemDocument
		{
			Id = entity.Id,
			RegionNumber = entity.RegionNumber,
			Name = entity.Name,
		};
	}

	public static PoolPlayGame ToEntity(
		PoolPlayGameDocument doc,
		int divisionId,
		Division? division = null,
		Pool? pool = null,
		Team? homeTeam = null,
		Team? awayTeam = null)
	{
		ArgumentNullException.ThrowIfNull(doc);

		return new PoolPlayGame
		{
			Id = doc.Id,
			DivisionId = divisionId,
			Division = division,
			PoolId = doc.PoolId,
			Pool = pool,
			Round = doc.Round,
			ScheduledDateTime = doc.ScheduledDateTime,
			Location = doc.Location,
			HomeTeamId = doc.HomeTeamId,
			HomeTeam = homeTeam,
			AwayTeamId = doc.AwayTeamId,
			AwayTeam = awayTeam,
			HomeGoals = doc.HomeGoals,
			AwayGoals = doc.AwayGoals,
			IsForfeit = doc.IsForfeit,
			HomeYellowCards = doc.HomeYellowCards,
			HomeRedCards = doc.HomeRedCards,
			AwayYellowCards = doc.AwayYellowCards,
			AwayRedCards = doc.AwayRedCards,
			ModifiedAt = doc.ModifiedAt,
			ModifiedBy = doc.ModifiedBy,
		};
	}

	public static PoolPlayGameDocument ToDocument(PoolPlayGame entity)
	{
		ArgumentNullException.ThrowIfNull(entity);

		return new PoolPlayGameDocument
		{
			Id = entity.Id,
			PoolId = entity.PoolId,
			Round = entity.Round,
			ScheduledDateTime = entity.ScheduledDateTime,
			Location = entity.Location,
			HomeTeamId = entity.HomeTeamId,
			AwayTeamId = entity.AwayTeamId,
			HomeGoals = entity.HomeGoals,
			AwayGoals = entity.AwayGoals,
			IsForfeit = entity.IsForfeit,
			HomeYellowCards = entity.HomeYellowCards,
			HomeRedCards = entity.HomeRedCards,
			AwayYellowCards = entity.AwayYellowCards,
			AwayRedCards = entity.AwayRedCards,
			ModifiedAt = entity.ModifiedAt,
			ModifiedBy = entity.ModifiedBy,
		};
	}

	public static ChampionshipGame ToEntity(
		ChampionshipGameDocument doc,
		int divisionId,
		Division? division = null,
		Team? homeTeam = null,
		Team? awayTeam = null)
	{
		ArgumentNullException.ThrowIfNull(doc);

		return new ChampionshipGame
		{
			Id = doc.Id,
			DivisionId = divisionId,
			Division = division,
			Slot = Enum.Parse<BracketSlot>(doc.Slot, ignoreCase: true),
			ScheduledDateTime = doc.ScheduledDateTime,
			Location = doc.Location,
			HomeSourceDescription = doc.HomeSourceDescription,
			AwaySourceDescription = doc.AwaySourceDescription,
			HomeTeamId = doc.HomeTeamId,
			HomeTeam = homeTeam,
			AwayTeamId = doc.AwayTeamId,
			AwayTeam = awayTeam,
			HomeGoals = doc.HomeGoals,
			AwayGoals = doc.AwayGoals,
			IsForfeit = doc.IsForfeit,
			ModifiedAt = doc.ModifiedAt,
			ModifiedBy = doc.ModifiedBy,
		};
	}

	public static ChampionshipGameDocument ToDocument(ChampionshipGame entity)
	{
		ArgumentNullException.ThrowIfNull(entity);

		return new ChampionshipGameDocument
		{
			Id = entity.Id,
			Slot = entity.Slot.ToString(),
			ScheduledDateTime = entity.ScheduledDateTime,
			Location = entity.Location,
			HomeSourceDescription = entity.HomeSourceDescription,
			AwaySourceDescription = entity.AwaySourceDescription,
			HomeTeamId = entity.HomeTeamId,
			AwayTeamId = entity.AwayTeamId,
			HomeGoals = entity.HomeGoals,
			AwayGoals = entity.AwayGoals,
			IsForfeit = entity.IsForfeit,
			ModifiedAt = entity.ModifiedAt,
			ModifiedBy = entity.ModifiedBy,
		};
	}

	public static ShootoutRound ToEntity(
		ShootoutRoundDocument doc,
		int divisionId,
		Division? division = null,
		PoolPlayGame? poolPlayGame = null,
		ChampionshipGame? championshipGame = null,
		Team? homeTeam = null,
		Team? awayTeam = null)
	{
		ArgumentNullException.ThrowIfNull(doc);

		var round = new ShootoutRound
		{
			Id = doc.Id,
			DivisionId = divisionId,
			Division = division,
			Round = doc.Round,
			PoolPlayGameId = doc.PoolPlayGameId,
			PoolPlayGame = poolPlayGame,
			ChampionshipGameId = doc.ChampionshipGameId,
			ChampionshipGame = championshipGame,
			HomeTeamId = doc.HomeTeamId,
			HomeTeam = homeTeam,
			AwayTeamId = doc.AwayTeamId,
			AwayTeam = awayTeam,
			HomePkGoals = doc.HomePkGoals,
			AwayPkGoals = doc.AwayPkGoals,
			ModifiedAt = doc.ModifiedAt,
			ModifiedBy = doc.ModifiedBy,
		};

		if (poolPlayGame is not null)
		{
			poolPlayGame.ShootoutRound = round;
		}

		if (championshipGame is not null)
		{
			championshipGame.ShootoutRound = round;
		}

		return round;
	}

	public static ShootoutRoundDocument ToDocument(ShootoutRound entity)
	{
		ArgumentNullException.ThrowIfNull(entity);

		return new ShootoutRoundDocument
		{
			Id = entity.Id,
			Round = entity.Round,
			PoolPlayGameId = entity.PoolPlayGameId,
			ChampionshipGameId = entity.ChampionshipGameId,
			HomeTeamId = entity.HomeTeamId,
			AwayTeamId = entity.AwayTeamId,
			HomePkGoals = entity.HomePkGoals,
			AwayPkGoals = entity.AwayPkGoals,
			ModifiedAt = entity.ModifiedAt,
			ModifiedBy = entity.ModifiedBy,
		};
	}

	public static TiebreakerOverride ToEntity(
		TiebreakerOverrideDocument doc,
		int divisionId,
		Division? division = null,
		Team? teamA = null,
		Team? teamB = null,
		Team? winningTeam = null)
	{
		ArgumentNullException.ThrowIfNull(doc);

		return new TiebreakerOverride
		{
			Id = doc.Id,
			DivisionId = divisionId,
			Division = division,
			Scope = Enum.Parse<TiebreakerScope>(doc.Scope, ignoreCase: true),
			TeamAId = doc.TeamAId,
			TeamA = teamA,
			TeamBId = doc.TeamBId,
			TeamB = teamB,
			WinningTeamId = doc.WinningTeamId,
			WinningTeam = winningTeam,
			ModifiedAt = doc.ModifiedAt,
			ModifiedBy = doc.ModifiedBy,
		};
	}

	public static TiebreakerOverrideDocument ToDocument(TiebreakerOverride entity)
	{
		ArgumentNullException.ThrowIfNull(entity);

		return new TiebreakerOverrideDocument
		{
			Id = entity.Id,
			Scope = entity.Scope.ToString(),
			TeamAId = entity.TeamAId,
			TeamBId = entity.TeamBId,
			WinningTeamId = entity.WinningTeamId,
			ModifiedAt = entity.ModifiedAt,
			ModifiedBy = entity.ModifiedBy,
		};
	}
}
