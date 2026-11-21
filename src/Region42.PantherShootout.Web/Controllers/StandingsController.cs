using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Helpers;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;
using Region42.PantherShootout.Domain.Interfaces;
using Region42.PantherShootout.Web.Helpers;
using Region42.PantherShootout.Web.Models;

namespace Region42.PantherShootout.Web.Controllers;

[AllowAnonymous]
public class StandingsController : Controller
{
	private readonly ITournamentYearService _tournamentYearService;
	private readonly IPoolPlayStandingsService _poolPlayStandingsService;
	private readonly IChampionshipBracketService _bracketService;
	private readonly IPsoStandingsService _psoStandingsService;
	private readonly ITournamentDataContext _dataContext;

	public StandingsController(
		ITournamentYearService tournamentYearService,
		IPoolPlayStandingsService poolPlayStandingsService,
		IChampionshipBracketService bracketService,
		IPsoStandingsService psoStandingsService,
		ITournamentDataContext dataContext)
	{
		_tournamentYearService = tournamentYearService ?? throw new ArgumentNullException(nameof(tournamentYearService));
		_poolPlayStandingsService = poolPlayStandingsService ?? throw new ArgumentNullException(nameof(poolPlayStandingsService));
		_bracketService = bracketService ?? throw new ArgumentNullException(nameof(bracketService));
		_psoStandingsService = psoStandingsService ?? throw new ArgumentNullException(nameof(psoStandingsService));
		_dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
	}

	[HttpGet("/standings")]
	public async Task<IActionResult> Index(int? year = null, CancellationToken cancellationToken = default)
	{
		var resolvedYear = year.HasValue
			? await _tournamentYearService.GetByYearNumberAsync(year.Value, cancellationToken)
			: await _tournamentYearService.GetDefaultYearAsync(cancellationToken);

		if (resolvedYear is null)
		{
			return RedirectToAction("Index", "Home");
		}

		return await RedirectToPreferredOrFirstDivisionAsync(resolvedYear, cancellationToken);
	}

	[HttpGet("/standings/{year:int}")]
	public async Task<IActionResult> YearIndex(int year, CancellationToken cancellationToken = default)
	{
		if (year < TournamentYearServiceAccessor.MinYear || year > TournamentYearServiceAccessor.MaxYear)
		{
			return NotFound();
		}

		var resolvedYear = await _tournamentYearService.GetByYearNumberAsync(year, cancellationToken);
		if (resolvedYear is null)
		{
			return NotFound();
		}

		return await RedirectToPreferredOrFirstDivisionAsync(resolvedYear, cancellationToken);
	}

	[HttpGet("/standings/{year:int}/{divisionKey}")]
	public async Task<IActionResult> Division(
		int year,
		string divisionKey,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(divisionKey) ||
			year < TournamentYearServiceAccessor.MinYear ||
			year > TournamentYearServiceAccessor.MaxYear)
		{
			return NotFound();
		}

		var allYears = await _tournamentYearService.GetAllYearsAsync(cancellationToken);
		var selectedYear = allYears.FirstOrDefault(y => y.Year == year);
		if (selectedYear is null)
		{
			return NotFound();
		}

		var siblingDivisions = await _tournamentYearService.GetDivisionsForYearAsync(selectedYear.Id, cancellationToken);
		var selectedDivision = siblingDivisions.FirstOrDefault(d =>
			string.Equals(d.DivisionKey, divisionKey.Trim(), StringComparison.OrdinalIgnoreCase));

		if (selectedDivision is null)
		{
			return NotFound();
		}

		DivisionPreferenceCookieHelper.SetPreferredDivisionKey(HttpContext, selectedDivision.DivisionKey);

		await _dataContext.LoadDivisionDataAsync(selectedDivision.Id, cancellationToken);

		var poolStandings = await _poolPlayStandingsService.CalculateForDivisionAsync(selectedDivision.Id, cancellationToken);
		var bracketResult = await _bracketService.SeedAndProgressAsync(selectedDivision.Id, cancellationToken);
		var psoResult = await _psoStandingsService.CalculateForDivisionAsync(selectedDivision.Id, cancellationToken);

		var teamsById = _dataContext.Teams.ToDictionary(t => t.Id);
		var poolsById = _dataContext.Pools
			.Where(p => p.DivisionId == selectedDivision.Id)
			.ToDictionary(p => p.Id, p => p.Name);
		var shootoutRounds = _dataContext.ShootoutRounds
			.Where(r => r.DivisionId == selectedDivision.Id)
			.ToList();

		var snapshot = _dataContext.GetStandingsForDivision(selectedDivision.Id);
		var championTeamId = bracketResult.ChampionTeamId ?? snapshot?.ChampionTeamId;

		var advancingTeamIds = DetermineAdvancingTeamIds(selectedDivision.Size, poolStandings);

		var poolTables = poolStandings.Pools
			.Select(p => new PoolStandingsTableViewModel
			{
				Title = poolStandings.Pools.Count == 1 ? $"{selectedDivision.Name} Pool Standings" : $"Pool {p.PoolName}",
				Result = p,
				AdvancingTeamIds = advancingTeamIds,
				ChampionTeamId = selectedDivision.Size == DivisionSize.Five ? championTeamId : null,
				ShowPoolBadgeOnTeam = false,
			})
			.ToList();

		PoolStandingsTableViewModel? combinedTenTeamTable = null;
		if (selectedDivision.Size == DivisionSize.Ten && poolStandings.CombinedDivisionStandings is not null)
		{
			combinedTenTeamTable = new PoolStandingsTableViewModel
			{
				Title = "Combined Division Standings (Determines Champion)",
				Subtitle = "10-team divisions have no championship bracket — all 10 teams are ranked together across both pools.",
				Result = poolStandings.CombinedDivisionStandings,
				ChampionTeamId = championTeamId,
				ShowPoolBadgeOnTeam = true,
			};
		}

		PoolStandingsTableViewModel? winnersTable = null;
		PoolStandingsTableViewModel? runnersUpTable = null;
		if (selectedDivision.Size == DivisionSize.Twelve)
		{
			if (poolStandings.PoolWinnersStandings is not null)
			{
				var winnerAdvIds = poolStandings.PoolWinnersStandings.Standings
					.Where(s => advancingTeamIds.Contains(s.TeamId))
					.Select(s => s.TeamId)
					.ToHashSet();
				winnersTable = new PoolStandingsTableViewModel
				{
					Title = "Pool Winners Sub-Ranking",
					Subtitle = "#1 & #2 play in Championship Final; #3 plays #1 Runner-Up for 3rd Place.",
					Result = poolStandings.PoolWinnersStandings,
					AdvancingTeamIds = winnerAdvIds,
					ShowPoolBadgeOnTeam = true,
				};
			}

			if (poolStandings.RunnersUpStandings is not null)
			{
				var topRunnerUpIds = poolStandings.RunnersUpStandings.Standings
					.Where(s => advancingTeamIds.Contains(s.TeamId))
					.Select(s => s.TeamId)
					.ToHashSet();
				runnersUpTable = new PoolStandingsTableViewModel
				{
					Title = "Pool Runners-Up Wildcard Sub-Ranking",
					Subtitle = "Only #1 Runner-Up advances (plays #3 Pool Winner for 3rd Place).",
					Result = poolStandings.RunnersUpStandings,
					AdvancingTeamIds = topRunnerUpIds,
					ShowPoolBadgeOnTeam = true,
				};
			}
		}

		var scheduleVm = BuildScheduleViewModel(
			selectedDivision.Id,
			teamsById,
			poolsById,
			shootoutRounds);

		var bracketVm = BuildBracketViewModel(
			selectedDivision,
			bracketResult,
			championTeamId,
			teamsById,
			shootoutRounds);

		var psoVm = BuildDivisionPsoViewModel(
			selectedYear.Year,
			selectedDivision,
			psoResult,
			teamsById,
			shootoutRounds,
			showFullShootoutLink: true);

		var vm = new DivisionStandingsViewModel
		{
			SelectedYear = selectedYear,
			AvailableYears = allYears.ToList(),
			SelectedDivision = selectedDivision,
			SiblingDivisions = siblingDivisions.ToList(),
			PoolStandings = poolStandings,
			PoolTables = poolTables,
			CombinedTenTeamTable = combinedTenTeamTable,
			TwelveTeamWinnersTable = winnersTable,
			TwelveTeamRunnersUpTable = runnersUpTable,
			Schedule = scheduleVm,
			Bracket = bracketVm,
			Pso = psoVm,
			CalculatedAtUtc = snapshot?.CalculatedAt,
		};

		return View(vm);
	}

	public static BracketTopologyKind GetBracketTopology(DivisionSize size)
	{
		return size switch
		{
			DivisionSize.Five or DivisionSize.Ten => BracketTopologyKind.NoneChampionBannerOnly,
			DivisionSize.Six => BracketTopologyKind.SemifinalsToFinal,
			_ => BracketTopologyKind.DirectFinals,
		};
	}

	public static DivisionPsoTableViewModel BuildDivisionPsoViewModel(
		int year,
		DivisionSummaryDto division,
		PsoStandingsResult psoResult,
		IReadOnlyDictionary<int, Team> teamsById,
		IReadOnlyList<ShootoutRound> shootoutRounds,
		bool showFullShootoutLink)
	{
		var scheduledRounds = DivisionSizeRules.GetShootoutRoundCount(division.Size);
		var usesCap = DivisionSizeRules.UsesPsoFourthRoundCap(division.Size);

		var cappedTeamIds = new HashSet<int>();
		if (usesCap)
		{
			foreach (var row in psoResult.Standings)
			{
				var earlyCompleted = shootoutRounds.Count(r =>
					r.Round <= 3 &&
					r.IsComplete &&
					(r.HomeTeamId == row.TeamId || r.AwayTeamId == row.TeamId));

				var hasRound4Complete = shootoutRounds.Any(r =>
					r.Round == 4 &&
					r.IsComplete &&
					(r.HomeTeamId == row.TeamId || r.AwayTeamId == row.TeamId));

				if (earlyCompleted >= DivisionSizeRules.RequiredPsoMatchupsPerTeam && hasRound4Complete)
				{
					cappedTeamIds.Add(row.TeamId);
				}
			}
		}

		string? leaderDisplay = null;
		if (psoResult.WinnerTeamId is int winnerId && teamsById.TryGetValue(winnerId, out var winner))
		{
			leaderDisplay = $"{winner.Code} {winner.Name}";
		}
		else if (psoResult.Standings.Any(s => s.RoundsCompleted > 0) && !HasTopPsoTie(psoResult))
		{
			var top = psoResult.Standings[0];
			leaderDisplay = $"{top.TeamDesignation} {top.TeamName}";
		}

		return new DivisionPsoTableViewModel
		{
			Year = year,
			DivisionKey = division.DivisionKey,
			DivisionName = division.Name,
			Size = division.Size,
			ScheduledRoundCount = scheduledRounds,
			UsesFourthRoundCap = usesCap,
			Result = psoResult,
			LeaderOrWinnerDisplay = leaderDisplay,
			ShowFullShootoutLink = showFullShootoutLink,
			CappedFourthRoundTeamIds = cappedTeamIds,
		};
	}

	private static bool HasTopPsoTie(PsoStandingsResult psoResult)
	{
		if (psoResult.Standings.Count < 2)
		{
			return false;
		}

		var first = psoResult.Standings[0];
		var second = psoResult.Standings[1];
		return first.TotalPkGoalsFor == second.TotalPkGoalsFor &&
			first.TotalPkGoalsAgainst == second.TotalPkGoalsAgainst &&
			psoResult.TiedTeamIdsForKftm.Contains(first.TeamId);
	}

	private async Task<IActionResult> RedirectToPreferredOrFirstDivisionAsync(
		TournamentYearDto year,
		CancellationToken cancellationToken)
	{
		var divisions = await _tournamentYearService.GetDivisionsForYearAsync(year.Id, cancellationToken);
		if (divisions.Count == 0)
		{
			return RedirectToAction("Index", "Home", new { year = year.Year });
		}

		var preferredKey = DivisionPreferenceCookieHelper.GetPreferredDivisionKey(HttpContext);
		var target = divisions.FirstOrDefault(d =>
			string.Equals(d.DivisionKey, preferredKey, StringComparison.OrdinalIgnoreCase))
			?? divisions[0];

		return RedirectToAction(nameof(Division), new
		{
			year = year.Year,
			divisionKey = target.DivisionKey,
		});
	}

	private static HashSet<int> DetermineAdvancingTeamIds(
		DivisionSize size,
		DivisionPoolPlayStandingsResult poolStandings)
	{
		var advancing = new HashSet<int>();
		if (!poolStandings.AllPoolsFinal)
		{
			return advancing;
		}

		switch (size)
		{
			case DivisionSize.Four:
			case DivisionSize.Six:
			case DivisionSize.Eight:
				foreach (var pool in poolStandings.Pools)
				{
					AddClinchedTopRanks(pool, cutoffRank: 2, advancing);
				}
				break;

			case DivisionSize.Twelve:
				if (poolStandings.Pools.Any(p => HasCutoffTie(p, cutoffRank: 1)))
				{
					break;
				}

				if (poolStandings.PoolWinnersStandings is not null)
				{
					foreach (var w in poolStandings.PoolWinnersStandings.Standings)
					{
						advancing.Add(w.TeamId);
					}
				}

				if (poolStandings.RunnersUpStandings is not null &&
					!poolStandings.Pools.Any(p => HasCutoffTie(p, cutoffRank: 2)))
				{
					AddClinchedTopRanks(poolStandings.RunnersUpStandings, cutoffRank: 1, advancing);
				}
				break;
		}

		return advancing;
	}

	private static void AddClinchedTopRanks(
		PoolPlayStandingsResult table,
		int cutoffRank,
		HashSet<int> advancing)
	{
		var hasBoundaryTie = HasCutoffTie(table, cutoffRank);
		foreach (var row in table.Standings.Where(s => s.Rank <= cutoffRank))
		{
			if (hasBoundaryTie && table.TiedTeamIdsForKftm.Contains(row.TeamId))
			{
				continue;
			}

			advancing.Add(row.TeamId);
		}
	}

	private static bool HasCutoffTie(PoolPlayStandingsResult table, int cutoffRank)
	{
		if (!table.RequiresKftmTiebreaker || table.TiedTeamIdsForKftm.Count < 2)
		{
			return false;
		}

		var tiedSet = table.TiedTeamIdsForKftm.ToHashSet();
		var hasInside = table.Standings.Any(s => s.Rank <= cutoffRank && tiedSet.Contains(s.TeamId));
		var hasOutside = table.Standings.Any(s => s.Rank > cutoffRank && tiedSet.Contains(s.TeamId));
		return hasInside && hasOutside;
	}

	private GameScheduleListViewModel BuildScheduleViewModel(
		int divisionId,
		IReadOnlyDictionary<int, Team> teamsById,
		IReadOnlyDictionary<int, string> poolsById,
		IReadOnlyList<ShootoutRound> shootoutRounds)
	{
		var psoByPoolGameId = shootoutRounds
			.Where(r => r.PoolPlayGameId.HasValue)
			.GroupBy(r => r.PoolPlayGameId!.Value)
			.ToDictionary(g => g.Key, g => g.First());

		var items = _dataContext.PoolPlayGames
			.Where(g => g.DivisionId == divisionId)
			.OrderBy(g => g.Round)
			.ThenBy(g => g.ScheduledDateTime ?? DateTime.MaxValue)
			.ThenBy(g => g.Location)
			.Select(g =>
			{
				teamsById.TryGetValue(g.HomeTeamId, out var home);
				teamsById.TryGetValue(g.AwayTeamId, out var away);
				psoByPoolGameId.TryGetValue(g.Id, out var pso);
				var poolName = poolsById.GetValueOrDefault(g.PoolId, string.Empty);

				return new GameScheduleCardItemViewModel
				{
					GameId = g.Id,
					Round = g.Round,
					PoolName = poolName,
					ScheduledDateTime = g.ScheduledDateTime,
					Location = g.Location,
					HomeTeamId = g.HomeTeamId,
					HomeCode = home?.Code ?? string.Empty,
					HomeShortName = FormatCompactTeam(home),
					HomeFullName = FormatFullTeam(home),
					AwayTeamId = g.AwayTeamId,
					AwayCode = away?.Code ?? string.Empty,
					AwayShortName = FormatCompactTeam(away),
					AwayFullName = FormatFullTeam(away),
					HomeGoals = g.HomeGoals,
					AwayGoals = g.AwayGoals,
					IsForfeit = g.IsForfeit,
					HomeYellowCards = g.HomeYellowCards,
					HomeRedCards = g.HomeRedCards,
					AwayYellowCards = g.AwayYellowCards,
					AwayRedCards = g.AwayRedCards,
					HomePkGoals = pso?.HomePkGoals,
					AwayPkGoals = pso?.AwayPkGoals,
				};
			})
			.ToList();

		var poolNames = poolsById.Values.OrderBy(n => n).ToList();

		return new GameScheduleListViewModel
		{
			PoolNames = poolNames,
			Games = items,
		};
	}

	private static ChampionshipBracketSectionViewModel BuildBracketViewModel(
		DivisionSummaryDto division,
		ChampionshipBracketResult bracketResult,
		int? championTeamId,
		IReadOnlyDictionary<int, Team> teamsById,
		IReadOnlyList<ShootoutRound> shootoutRounds)
	{
		var psoByChampGameId = shootoutRounds
			.Where(r => r.ChampionshipGameId.HasValue)
			.GroupBy(r => r.ChampionshipGameId!.Value)
			.ToDictionary(g => g.Key, g => g.First());

		string? championDisplay = null;
		if (championTeamId.HasValue && teamsById.TryGetValue(championTeamId.Value, out var champTeam))
		{
			championDisplay = FormatFullTeam(champTeam);
		}

		var matches = bracketResult.Games
			.Select(g =>
			{
				Team? homeTeam = g.HomeTeamId.HasValue ? teamsById.GetValueOrDefault(g.HomeTeamId.Value) : null;
				Team? awayTeam = g.AwayTeamId.HasValue ? teamsById.GetValueOrDefault(g.AwayTeamId.Value) : null;
				ShootoutRound? r4Pso = g.GameId.HasValue ? psoByChampGameId.GetValueOrDefault(g.GameId.Value) : null;

				return new BracketMatchCardViewModel
				{
					GameId = g.GameId,
					Slot = g.Slot,
					SlotLabel = ImportController.FormatBracketSlotLabel(g.Slot),
					ScheduledDateTime = g.ScheduledDateTime,
					Location = g.Location,
					HomeTeamId = g.HomeTeamId,
					HomeShortDisplay = homeTeam is not null ? FormatCompactTeam(homeTeam) : g.HomeSourceDescription,
					HomeFullDisplay = homeTeam is not null ? FormatFullTeam(homeTeam) : g.HomeSourceDescription,
					IsHomeSeeded = homeTeam is not null,
					AwayTeamId = g.AwayTeamId,
					AwayShortDisplay = awayTeam is not null ? FormatCompactTeam(awayTeam) : g.AwaySourceDescription,
					AwayFullDisplay = awayTeam is not null ? FormatFullTeam(awayTeam) : g.AwaySourceDescription,
					IsAwaySeeded = awayTeam is not null,
					HomeGoals = g.HomeGoals,
					AwayGoals = g.AwayGoals,
					IsForfeit = g.IsForfeit,
					WinnerTeamId = g.WinnerTeamId,
					HomePkGoals = r4Pso?.HomePkGoals,
					AwayPkGoals = r4Pso?.AwayPkGoals,
				};
			})
			.ToList();

		return new ChampionshipBracketSectionViewModel
		{
			DivisionName = division.Name,
			Size = division.Size,
			Topology = GetBracketTopology(division.Size),
			PoolPlayFinal = bracketResult.PoolPlayFinal,
			ChampionTeamId = championTeamId,
			ChampionTeamDisplay = championDisplay,
			Matches = matches,
		};
	}

	private static string FormatCompactTeam(Team? team)
	{
		if (team is null)
		{
			return "TBD";
		}

		var coachOrName = !string.IsNullOrWhiteSpace(team.CoachName) ? team.CoachName : team.Name;
		return string.IsNullOrWhiteSpace(team.Code) ? coachOrName : $"{team.Code} {coachOrName}";
	}

	private static string FormatFullTeam(Team? team)
	{
		if (team is null)
		{
			return "TBD";
		}

		return string.IsNullOrWhiteSpace(team.Code) ? team.Name : $"{team.Code} {team.Name}";
	}

	private static class TournamentYearServiceAccessor
	{
		public const int MinYear = 2000;
		public const int MaxYear = 2100;
	}
}
