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
	private const int MinYear = 2000;
	private const int MaxYear = 2100;

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
		var capAnalysis = PsoFourthRoundCapHelper.Analyze(division.Size, shootoutRounds);
		var (_, leaderDisplay) = ResolvePsoLeader(psoResult, teamsById);

		return new DivisionPsoTableViewModel
		{
			Year = year,
			DivisionKey = division.DivisionKey,
			DivisionName = division.Name,
			Size = division.Size,
			ScheduledRoundCount = scheduledRounds,
			HasFourthShootoutRound = capAnalysis.HasFourthShootoutRound,
			UsesFourthRoundCap = usesCap,
			Result = psoResult,
			LeaderOrWinnerDisplay = leaderDisplay,
			ShowFullShootoutLink = showFullShootoutLink,
			CappedFourthRoundTeamIds = capAnalysis.NonCountingFourthRoundTeamIds,
			ExcludedFourthRoundScoresByTeamId = capAnalysis.ExcludedFourthRoundScoresByTeamId,
		};
	}

	private static (int? LeaderTeamId, string? LeaderDisplay) ResolvePsoLeader(
		PsoStandingsResult psoResult,
		IReadOnlyDictionary<int, Team> teamsById)
	{
		if (psoResult.WinnerTeamId is int winnerId && teamsById.TryGetValue(winnerId, out var winner))
		{
			return (winnerId, $"{winner.Code} {winner.Name}");
		}

		if (psoResult.Standings.Any(s => s.RoundsCompleted > 0) && !HasTopPsoTie(psoResult))
		{
			var top = psoResult.Standings[0];
			return (top.TeamId, $"{top.TeamDesignation} {top.TeamName}");
		}

		return (null, null);
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

	public static string FormatPrintTeam(Team? team)
	{
		if (team is null)
		{
			return "TBD";
		}

		var coachAndRegion = FormatCoachWithRegionNumber(team.CoachName, team.Name, team.RegionNumber);
		return string.IsNullOrWhiteSpace(team.Code)
			? coachAndRegion
			: $"{team.Code}: {coachAndRegion}";
	}

	public static string FormatCoachWithRegionNumber(string? coachName, string fallbackName, int regionNumber)
	{
		var label = !string.IsNullOrWhiteSpace(coachName) ? coachName.Trim() : fallbackName.Trim();
		return regionNumber > 0 ? $"{label} ({regionNumber})" : label;
	}

	public static string ExtractFieldNumberOrLabel(string? location)
	{
		if (string.IsNullOrWhiteSpace(location))
		{
			return string.Empty;
		}

		var trimmed = location.Trim();
		const string fieldPrefix = "Field ";
		if (trimmed.StartsWith(fieldPrefix, StringComparison.OrdinalIgnoreCase) && trimmed.Length > fieldPrefix.Length)
		{
			return trimmed[fieldPrefix.Length..].Trim();
		}

		return trimmed;
	}

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
		if (year < MinYear || year > MaxYear)
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

	[HttpGet("/standings/{year:int}/{divisionKey}")]
	public async Task<IActionResult> Division(
		int year,
		string divisionKey,
		CancellationToken cancellationToken = default)
	{
		var ctx = await LoadDivisionPageDataAsync(year, divisionKey, cancellationToken);
		if (ctx is null)
		{
			return NotFound();
		}

		var poolTables = ctx.PoolStandings.Pools
			.Select(p => new PoolStandingsTableViewModel
			{
				Title = ctx.PoolStandings.Pools.Count == 1 ? $"{ctx.SelectedDivision.Name} Pool Standings" : $"Pool {p.PoolName}",
				Result = p,
				AdvancingTeamIds = ctx.AdvancingTeamIds,
				ChampionTeamId = ctx.SelectedDivision.Size == DivisionSize.Five ? ctx.ChampionTeamId : null,
				ShowPoolBadgeOnTeam = false,
				PrintPoolUrl = BuildPoolPrintUrl(ctx.SelectedYear.Year, ctx.SelectedDivision.DivisionKey, p.PoolName),
			})
			.ToList();

		PoolStandingsTableViewModel? combinedTenTeamTable = null;
		if (ctx.SelectedDivision.Size == DivisionSize.Ten && ctx.PoolStandings.CombinedDivisionStandings is not null)
		{
			combinedTenTeamTable = new PoolStandingsTableViewModel
			{
				Title = "Combined Division Standings (Determines Champion)",
				Subtitle = "10-team divisions have no championship bracket — all 10 teams are ranked together across both pools.",
				Result = ctx.PoolStandings.CombinedDivisionStandings,
				ChampionTeamId = ctx.ChampionTeamId,
				ShowPoolBadgeOnTeam = true,
			};
		}

		PoolStandingsTableViewModel? winnersTable = null;
		PoolStandingsTableViewModel? runnersUpTable = null;
		if (ctx.SelectedDivision.Size == DivisionSize.Twelve)
		{
			if (ctx.PoolStandings.PoolWinnersStandings is not null)
			{
				var winnerAdvIds = ctx.PoolStandings.PoolWinnersStandings.Standings
					.Where(s => ctx.AdvancingTeamIds.Contains(s.TeamId))
					.Select(s => s.TeamId)
					.ToHashSet();
				winnersTable = new PoolStandingsTableViewModel
				{
					Title = "Pool Winners Sub-Ranking",
					Subtitle = "#1 & #2 play in Championship Final; #3 plays #1 Runner-Up for 3rd Place.",
					Result = ctx.PoolStandings.PoolWinnersStandings,
					AdvancingTeamIds = winnerAdvIds,
					ShowPoolBadgeOnTeam = true,
				};
			}

			if (ctx.PoolStandings.RunnersUpStandings is not null)
			{
				var topRunnerUpIds = ctx.PoolStandings.RunnersUpStandings.Standings
					.Where(s => ctx.AdvancingTeamIds.Contains(s.TeamId))
					.Select(s => s.TeamId)
					.ToHashSet();
				runnersUpTable = new PoolStandingsTableViewModel
				{
					Title = "Pool Runners-Up Wildcard Sub-Ranking",
					Subtitle = "Only #1 Runner-Up advances (plays #3 Pool Winner for 3rd Place).",
					Result = ctx.PoolStandings.RunnersUpStandings,
					AdvancingTeamIds = topRunnerUpIds,
					ShowPoolBadgeOnTeam = true,
				};
			}
		}

		var bracketVm = BuildBracketViewModel(
			ctx.SelectedDivision,
			ctx.BracketResult,
			ctx.ChampionTeamId,
			ctx.TeamsById,
			ctx.ShootoutRounds,
			ctx.PsoCapAnalysis);

		var psoVm = BuildDivisionPsoViewModel(
			ctx.SelectedYear.Year,
			ctx.SelectedDivision,
			ctx.PsoResult,
			ctx.TeamsById,
			ctx.ShootoutRounds,
			showFullShootoutLink: true);

		var vm = new DivisionStandingsViewModel
		{
			SelectedYear = ctx.SelectedYear,
			AvailableYears = ctx.AllYears,
			SelectedDivision = ctx.SelectedDivision,
			SiblingDivisions = ctx.SiblingDivisions,
			PoolStandings = ctx.PoolStandings,
			PoolTables = poolTables,
			CombinedTenTeamTable = combinedTenTeamTable,
			TwelveTeamWinnersTable = winnersTable,
			TwelveTeamRunnersUpTable = runnersUpTable,
			Schedule = ctx.Schedule,
			Bracket = bracketVm,
			Pso = psoVm,
			CalculatedAtUtc = ctx.CalculatedAtUtc,
		};

		return View(vm);
	}

	[HttpGet("/standings/{year:int}/{divisionKey}/print")]
	public async Task<IActionResult> Print(
		int year,
		string divisionKey,
		string? pool = null,
		CancellationToken cancellationToken = default)
	{
		var ctx = await LoadDivisionPageDataAsync(year, divisionKey, cancellationToken);
		if (ctx is null)
		{
			return NotFound();
		}

		var publicUrl = QrCodeHelper.BuildAbsoluteUrl(
			Request,
			$"/standings/{ctx.SelectedYear.Year}/{ctx.SelectedDivision.DivisionKey}");

		var printVm = BuildPrintViewModel(ctx, pool, publicUrl);
		return View(printVm);
	}

	private async Task<LoadedDivisionContext?> LoadDivisionPageDataAsync(
		int year,
		string divisionKey,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(divisionKey) || year < MinYear || year > MaxYear)
		{
			return null;
		}

		var allYears = await _tournamentYearService.GetAllYearsAsync(cancellationToken);
		var selectedYear = allYears.FirstOrDefault(y => y.Year == year);
		if (selectedYear is null)
		{
			return null;
		}

		var siblingDivisions = await _tournamentYearService.GetDivisionsForYearAsync(selectedYear.Id, cancellationToken);
		var selectedDivision = siblingDivisions.FirstOrDefault(d =>
			string.Equals(d.DivisionKey, divisionKey.Trim(), StringComparison.OrdinalIgnoreCase));

		if (selectedDivision is null)
		{
			return null;
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
		var psoCapAnalysis = PsoFourthRoundCapHelper.Analyze(selectedDivision.Size, shootoutRounds);
		var scheduleVm = BuildScheduleViewModel(selectedDivision.Id, teamsById, poolsById, shootoutRounds, psoCapAnalysis);

		return new LoadedDivisionContext(
			selectedYear,
			allYears.ToList(),
			selectedDivision,
			siblingDivisions.ToList(),
			poolStandings,
			bracketResult,
			psoResult,
			psoCapAnalysis,
			teamsById,
			shootoutRounds,
			championTeamId,
			advancingTeamIds,
			scheduleVm,
			snapshot?.CalculatedAt);
	}

	private static string BuildPoolPrintUrl(int year, string divisionKey, string poolName)
	{
		var baseUrl = $"/standings/{year}/{divisionKey}/print";
		return string.IsNullOrWhiteSpace(poolName)
			? baseUrl
			: $"{baseUrl}?pool={Uri.EscapeDataString(poolName)}";
	}

	private static DivisionPrintViewModel BuildPrintViewModel(
		LoadedDivisionContext ctx,
		string? requestedPool,
		string publicDivisionUrl)
	{
		var (psoLeaderTeamId, psoLeaderDisplay) = ResolvePsoLeader(ctx.PsoResult, ctx.TeamsById);
		var psoByTeamId = ctx.PsoResult.Standings.ToDictionary(s => s.TeamId);

		var expectedTeamsPerPool = DivisionSizeRules.GetTeamsPerPool(ctx.SelectedDivision.Size);
		var expectedTotalRounds = DivisionSizeRules.GetPoolPlayRoundCount(ctx.SelectedDivision.Size);
		var noChampionshipBracket = !DivisionSizeRules.HasChampionshipBracket(ctx.SelectedDivision.Size);

		string? matchedPoolFilter = null;
		if (!string.IsNullOrWhiteSpace(requestedPool))
		{
			var trimmedPool = requestedPool.Trim();
			var matchedPool = ctx.PoolStandings.Pools.FirstOrDefault(p =>
				string.Equals(p.PoolName, trimmedPool, StringComparison.OrdinalIgnoreCase));
			if (matchedPool is not null && !string.IsNullOrWhiteSpace(matchedPool.PoolName))
			{
				matchedPoolFilter = matchedPool.PoolName;
			}
		}

		var isSinglePoolDivision = ctx.PoolStandings.Pools.Count <= 1;
		var sheets = new List<PoolPrintSheetViewModel>(ctx.PoolStandings.Pools.Count);

		foreach (var poolResult in ctx.PoolStandings.Pools)
		{
			var poolGames = ctx.Schedule.Games
				.Where(g => string.Equals(g.PoolName, poolResult.PoolName, StringComparison.OrdinalIgnoreCase)
					|| (isSinglePoolDivision && string.IsNullOrEmpty(g.PoolName)))
				.ToList();

			var maxScheduledRound = poolGames.Count > 0 ? poolGames.Max(g => g.Round) : 0;
			var totalRounds = Math.Max(expectedTotalRounds, maxScheduledRound);
			var completedRounds = poolGames
				.GroupBy(g => g.Round)
				.Count(rg => rg.Any() && rg.All(g => g.HasScore));

			var teamCount = poolResult.Standings.Count > 0
				? Math.Clamp(poolResult.Standings.Count, 3, 5)
				: expectedTeamsPerPool;

			var headerTitle = isSinglePoolDivision || string.IsNullOrWhiteSpace(poolResult.PoolName)
				? ctx.SelectedDivision.Name
				: $"{ctx.SelectedDivision.Name} — Pool {poolResult.PoolName}";

			var rows = poolResult.Standings
				.Select(row =>
				{
					psoByTeamId.TryGetValue(row.TeamId, out var psoRow);
					var isChamp = noChampionshipBracket
						&& ctx.ChampionTeamId.HasValue
						&& ctx.ChampionTeamId.Value == row.TeamId;
					var advances = !isChamp && ctx.AdvancingTeamIds.Contains(row.TeamId);

					return new PoolPrintStandingRowViewModel
					{
						Standing = row,
						PrintCoachAndRegion = FormatCoachWithRegionNumber(row.CoachName, row.TeamName, row.RegionNumber),
						IsChampion = isChamp,
						Advances = advances,
						PsoGoalsFor = psoRow?.TotalPkGoalsFor ?? 0,
						PsoGoalsAgainst = psoRow?.TotalPkGoalsAgainst ?? 0,
						PsoRoundsCompleted = psoRow?.RoundsCompleted ?? 0,
						IsPsoDivisionLeader = psoLeaderTeamId.HasValue && psoLeaderTeamId.Value == row.TeamId,
					};
				})
				.ToList();

			var isHidden = matchedPoolFilter is not null
				&& !string.Equals(poolResult.PoolName, matchedPoolFilter, StringComparison.OrdinalIgnoreCase);

			sheets.Add(new PoolPrintSheetViewModel
			{
				PoolId = poolResult.PoolId,
				PoolName = poolResult.PoolName,
				HeaderTitle = headerTitle,
				TeamCount = teamCount,
				CompletedRounds = completedRounds,
				TotalRounds = totalRounds,
				IsPoolFinal = poolResult.IsFinal,
				RequiresKftmTiebreaker = poolResult.RequiresKftmTiebreaker,
				AnyForfeitInScope = poolResult.AnyForfeitInScope,
				StandingsRows = rows,
				Games = poolGames,
				IsHiddenByInitialFilter = isHidden,
				IsLastVisibleSheet = false,
			});
		}

		var lastVisible = sheets.LastOrDefault(s => !s.IsHiddenByInitialFilter);
		if (lastVisible is not null)
		{
			lastVisible.IsLastVisibleSheet = true;
		}

		return new DivisionPrintViewModel
		{
			SelectedYear = ctx.SelectedYear,
			SelectedDivision = ctx.SelectedDivision,
			SiblingDivisions = ctx.SiblingDivisions,
			SelectedPoolFilter = matchedPoolFilter,
			PsoLeaderDisplay = psoLeaderDisplay,
			PsoIsFinal = ctx.PsoResult.IsFinal && !ctx.PsoResult.RequiresKftmTiebreaker,
			HasFourthShootoutRound = ctx.PsoCapAnalysis.HasFourthShootoutRound,
			PublicDivisionUrl = publicDivisionUrl,
			PublicDivisionDisplayUrl = QrCodeHelper.FormatDisplayUrl(publicDivisionUrl),
			QrCodeDataUri = QrCodeHelper.GeneratePngDataUri(publicDivisionUrl),
			CalculatedAtUtc = ctx.CalculatedAtUtc,
			PoolSheets = sheets,
		};
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
		IReadOnlyList<ShootoutRound> shootoutRounds,
		PsoFourthRoundCapAnalysis psoCapAnalysis)
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
					FieldShortLabel = ExtractFieldNumberOrLabel(g.Location),
					HomeTeamId = g.HomeTeamId,
					HomeCode = home?.Code ?? string.Empty,
					HomeShortName = FormatCompactTeam(home),
					HomeFullName = FormatFullTeam(home),
					HomePrintName = FormatPrintTeam(home),
					AwayTeamId = g.AwayTeamId,
					AwayCode = away?.Code ?? string.Empty,
					AwayShortName = FormatCompactTeam(away),
					AwayFullName = FormatFullTeam(away),
					AwayPrintName = FormatPrintTeam(away),
					HomeGoals = g.HomeGoals,
					AwayGoals = g.AwayGoals,
					IsForfeit = g.IsForfeit,
					HomeYellowCards = g.HomeYellowCards,
					HomeRedCards = g.HomeRedCards,
					AwayYellowCards = g.AwayYellowCards,
					AwayRedCards = g.AwayRedCards,
					HomePkGoals = pso?.HomePkGoals,
					AwayPkGoals = pso?.AwayPkGoals,
					HasPairedShootoutRound = pso is not null,
					HomePsoDoesNotCount = pso is not null && psoCapAnalysis.IsTeamExcludedInRound(pso.Round, g.HomeTeamId),
					AwayPsoDoesNotCount = pso is not null && psoCapAnalysis.IsTeamExcludedInRound(pso.Round, g.AwayTeamId),
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
		IReadOnlyList<ShootoutRound> shootoutRounds,
		PsoFourthRoundCapAnalysis psoCapAnalysis)
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
					HasPairedRound4Pso = r4Pso is not null,
					HomePsoDoesNotCount = r4Pso is not null && psoCapAnalysis.IsTeamExcludedInRound(r4Pso.Round, g.HomeTeamId),
					AwayPsoDoesNotCount = r4Pso is not null && psoCapAnalysis.IsTeamExcludedInRound(r4Pso.Round, g.AwayTeamId),
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

	private sealed record LoadedDivisionContext(
		TournamentYearDto SelectedYear,
		List<TournamentYearDto> AllYears,
		DivisionSummaryDto SelectedDivision,
		List<DivisionSummaryDto> SiblingDivisions,
		DivisionPoolPlayStandingsResult PoolStandings,
		ChampionshipBracketResult BracketResult,
		PsoStandingsResult PsoResult,
		PsoFourthRoundCapAnalysis PsoCapAnalysis,
		IReadOnlyDictionary<int, Team> TeamsById,
		IReadOnlyList<ShootoutRound> ShootoutRounds,
		int? ChampionTeamId,
		HashSet<int> AdvancingTeamIds,
		GameScheduleListViewModel Schedule,
		DateTime? CalculatedAtUtc);
}
