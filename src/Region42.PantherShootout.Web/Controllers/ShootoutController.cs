using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Interfaces;
using Region42.PantherShootout.Web.Helpers;
using Region42.PantherShootout.Web.Models;

namespace Region42.PantherShootout.Web.Controllers;

[AllowAnonymous]
public class ShootoutController : Controller
{
	private readonly ITournamentYearService _tournamentYearService;
	private readonly IPsoStandingsService _psoStandingsService;
	private readonly ITournamentDataContext _dataContext;

	public ShootoutController(
		ITournamentYearService tournamentYearService,
		IPsoStandingsService psoStandingsService,
		ITournamentDataContext dataContext)
	{
		_tournamentYearService = tournamentYearService ?? throw new ArgumentNullException(nameof(tournamentYearService));
		_psoStandingsService = psoStandingsService ?? throw new ArgumentNullException(nameof(psoStandingsService));
		_dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
	}

	[HttpGet("/shootout/{year:int?}")]
	public async Task<IActionResult> Index(
		int? year = null,
		string? division = null,
		CancellationToken cancellationToken = default)
	{
		var allYears = await _tournamentYearService.GetAllYearsAsync(cancellationToken);

		TournamentYearDto? selectedYear = null;
		if (year.HasValue)
		{
			selectedYear = allYears.FirstOrDefault(y => y.Year == year.Value);
			if (selectedYear is null)
			{
				return NotFound();
			}
		}
		else
		{
			selectedYear = await _tournamentYearService.GetDefaultYearAsync(cancellationToken);
		}

		if (selectedYear is null)
		{
			return View(new ShootoutPageViewModel
			{
				AvailableYears = allYears.ToList(),
			});
		}

		var divisions = await _tournamentYearService.GetDivisionsForYearAsync(selectedYear.Id, cancellationToken);
		if (divisions.Count == 0)
		{
			return View(new ShootoutPageViewModel
			{
				SelectedYear = selectedYear,
				AvailableYears = allYears.ToList(),
			});
		}

		DivisionSummaryDto selectedDivision;
		if (!string.IsNullOrWhiteSpace(division))
		{
			var explicitMatch = divisions.FirstOrDefault(d =>
				string.Equals(d.DivisionKey, division.Trim(), StringComparison.OrdinalIgnoreCase));
			if (explicitMatch is null)
			{
				return NotFound();
			}

			selectedDivision = explicitMatch;
			DivisionPreferenceCookieHelper.SetPreferredDivisionKey(HttpContext, selectedDivision.DivisionKey);
		}
		else
		{
			var cookieKey = DivisionPreferenceCookieHelper.GetPreferredDivisionKey(HttpContext);
			selectedDivision = divisions.FirstOrDefault(d =>
				string.Equals(d.DivisionKey, cookieKey, StringComparison.OrdinalIgnoreCase))
				?? divisions.FirstOrDefault(d => string.Equals(d.DivisionKey, "10u-girls", StringComparison.OrdinalIgnoreCase))
				?? divisions[0];
		}

		await _dataContext.LoadDivisionDataAsync(selectedDivision.Id, cancellationToken);
		var psoResult = await _psoStandingsService.CalculateForDivisionAsync(selectedDivision.Id, cancellationToken);

		var teamsById = _dataContext.Teams.ToDictionary(t => t.Id);
		var poolsById = _dataContext.Pools
			.Where(p => p.DivisionId == selectedDivision.Id)
			.ToDictionary(p => p.Id, p => p.Name);
		var poolGamesById = _dataContext.PoolPlayGames
			.Where(g => g.DivisionId == selectedDivision.Id)
			.ToDictionary(g => g.Id);
		var champGamesById = _dataContext.ChampionshipGames
			.Where(g => g.DivisionId == selectedDivision.Id)
			.ToDictionary(g => g.Id);
		var shootoutRounds = _dataContext.ShootoutRounds
			.Where(r => r.DivisionId == selectedDivision.Id)
			.OrderBy(r => r.Round)
			.ThenBy(r => r.Id)
			.ToList();

		var psoTableVm = StandingsController.BuildDivisionPsoViewModel(
			selectedYear.Year,
			selectedDivision,
			psoResult,
			teamsById,
			shootoutRounds,
			showFullShootoutLink: false);

		var capAnalysis = PsoFourthRoundCapHelper.Analyze(selectedDivision.Size, shootoutRounds);

		var matchups = shootoutRounds
			.Select(r => BuildMatchupItem(r, teamsById, poolsById, poolGamesById, champGamesById, capAnalysis))
			.OrderBy(m => m.Round)
			.ThenBy(m => m.ScheduledDateTime ?? DateTime.MaxValue)
			.ThenBy(m => m.Location)
			.ToList();

		return View(new ShootoutPageViewModel
		{
			HasFourthShootoutRound = capAnalysis.HasFourthShootoutRound,
			SelectedYear = selectedYear,
			AvailableYears = allYears.ToList(),
			SelectedDivision = selectedDivision,
			Divisions = divisions.ToList(),
			PsoTable = psoTableVm,
			Matchups = matchups,
		});
	}

	private static PsoMatchupItemViewModel BuildMatchupItem(
		ShootoutRound round,
		IReadOnlyDictionary<int, Team> teamsById,
		IReadOnlyDictionary<int, string> poolsById,
		IReadOnlyDictionary<int, PoolPlayGame> poolGamesById,
		IReadOnlyDictionary<int, ChampionshipGame> champGamesById,
		PsoFourthRoundCapAnalysis capAnalysis)
	{
		DateTime? scheduledDateTime = null;
		var location = string.Empty;
		var sourceLabel = $"Round {round.Round}";
		var fallbackHome = "TBD";
		var fallbackAway = "TBD";

		if (round.PoolPlayGameId.HasValue && poolGamesById.TryGetValue(round.PoolPlayGameId.Value, out var pg))
		{
			scheduledDateTime = pg.ScheduledDateTime;
			location = pg.Location;
			var poolName = poolsById.GetValueOrDefault(pg.PoolId, string.Empty);
			sourceLabel = string.IsNullOrWhiteSpace(poolName) ? $"Pool Game" : $"Pool {poolName}";
		}
		else if (round.ChampionshipGameId.HasValue && champGamesById.TryGetValue(round.ChampionshipGameId.Value, out var cg))
		{
			scheduledDateTime = cg.ScheduledDateTime;
			location = cg.Location;
			sourceLabel = ImportController.FormatBracketSlotLabel(cg.Slot);
			fallbackHome = cg.HomeSourceDescription;
			fallbackAway = cg.AwaySourceDescription;
		}

		var homeTeam = round.HomeTeamId.HasValue ? teamsById.GetValueOrDefault(round.HomeTeamId.Value) : null;
		var awayTeam = round.AwayTeamId.HasValue ? teamsById.GetValueOrDefault(round.AwayTeamId.Value) : null;

		return new PsoMatchupItemViewModel
		{
			ShootoutRoundId = round.Id,
			Round = round.Round,
			SourceLabel = sourceLabel,
			ScheduledDateTime = scheduledDateTime,
			Location = location,
			IsSeeded = homeTeam is not null && awayTeam is not null,
			HomeCode = homeTeam?.Code ?? fallbackHome,
			HomeShortDisplay = homeTeam is not null ? $"{homeTeam.Code} {homeTeam.CoachName}" : fallbackHome,
			HomeFullDisplay = homeTeam is not null ? $"{homeTeam.Code} {homeTeam.Name}" : fallbackHome,
			AwayCode = awayTeam?.Code ?? fallbackAway,
			AwayShortDisplay = awayTeam is not null ? $"{awayTeam.Code} {awayTeam.CoachName}" : fallbackAway,
			AwayFullDisplay = awayTeam is not null ? $"{awayTeam.Code} {awayTeam.Name}" : fallbackAway,
			HomePkGoals = round.HomePkGoals,
			AwayPkGoals = round.AwayPkGoals,
			HomePsoDoesNotCount = capAnalysis.IsTeamExcludedInRound(round.Round, round.HomeTeamId),
			AwayPsoDoesNotCount = capAnalysis.IsTeamExcludedInRound(round.Round, round.AwayTeamId),
		};
	}
}
