using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Helpers;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;
using Region42.PantherShootout.Domain.Interfaces;
using Region42.PantherShootout.Web.Helpers;
using Region42.PantherShootout.Web.Models;

namespace Region42.PantherShootout.Web.Controllers;

[Authorize(Policy = "AdminPolicy")]
public class ScoresController : Controller
{
	private readonly ITournamentYearService _tournamentYearService;
	private readonly IPoolPlayStandingsService _poolPlayStandingsService;
	private readonly IChampionshipBracketService _bracketService;
	private readonly IPsoStandingsService _psoStandingsService;
	private readonly IScoreEntryService _scoreEntryService;
	private readonly ITournamentDataContext _dataContext;

	public ScoresController(
		ITournamentYearService tournamentYearService,
		IPoolPlayStandingsService poolPlayStandingsService,
		IChampionshipBracketService bracketService,
		IPsoStandingsService psoStandingsService,
		IScoreEntryService scoreEntryService,
		ITournamentDataContext dataContext)
	{
		_tournamentYearService = tournamentYearService ?? throw new ArgumentNullException(nameof(tournamentYearService));
		_poolPlayStandingsService = poolPlayStandingsService ?? throw new ArgumentNullException(nameof(poolPlayStandingsService));
		_bracketService = bracketService ?? throw new ArgumentNullException(nameof(bracketService));
		_psoStandingsService = psoStandingsService ?? throw new ArgumentNullException(nameof(psoStandingsService));
		_scoreEntryService = scoreEntryService ?? throw new ArgumentNullException(nameof(scoreEntryService));
		_dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
	}

	[HttpGet("/Scores")]
	[HttpGet("/Scores/Entry")]
	public async Task<IActionResult> Entry(
		int? year = null,
		string? division = null,
		int? round = null,
		string? tab = null,
		CancellationToken cancellationToken = default)
	{
		var allYears = await _tournamentYearService.GetAllYearsAsync(cancellationToken);
		var selectedYear = year.HasValue
			? allYears.FirstOrDefault(y => y.Year == year.Value)
			: await _tournamentYearService.GetDefaultYearAsync(cancellationToken);

		if (selectedYear is null)
		{
			return View(new ScoresEntryPageViewModel
			{
				AvailableYears = allYears.ToList(),
			});
		}

		var divisions = await _tournamentYearService.GetDivisionsForYearAsync(selectedYear.Id, cancellationToken);
		if (divisions.Count == 0)
		{
			return View(new ScoresEntryPageViewModel
			{
				SelectedYear = selectedYear,
				AvailableYears = allYears.ToList(),
			});
		}

		DivisionSummaryDto selectedDivision;
		if (!string.IsNullOrWhiteSpace(division))
		{
			selectedDivision = divisions.FirstOrDefault(d =>
				string.Equals(d.DivisionKey, division.Trim(), StringComparison.OrdinalIgnoreCase))
				?? divisions[0];
			DivisionPreferenceCookieHelper.SetPreferredDivisionKey(HttpContext, selectedDivision.DivisionKey);
		}
		else
		{
			var preferredKey = DivisionPreferenceCookieHelper.GetPreferredDivisionKey(HttpContext);
			selectedDivision = divisions.FirstOrDefault(d =>
				string.Equals(d.DivisionKey, preferredKey, StringComparison.OrdinalIgnoreCase))
				?? divisions[0];
		}

		var vm = await BuildPageViewModelAsync(
			selectedYear,
			allYears.ToList(),
			selectedDivision,
			divisions.ToList(),
			round,
			tab,
			cancellationToken);

		return View(vm);
	}

	[HttpPost("/Scores/SaveBatch")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> SaveBatch(
		DivisionScoresFormPostModel form,
		CancellationToken cancellationToken = default)
	{
		if (form is null || form.DivisionId <= 0)
		{
			return BadRequest();
		}

		try
		{
			await _scoreEntryService.SaveDivisionScoresBatchAsync(new DivisionScoreBatchUpdateDto
			{
				DivisionId = form.DivisionId,
				PoolPlayScores = form.PoolPlayScores ?? [],
				ChampionshipScores = form.ChampionshipScores ?? [],
				PsoScores = form.PsoScores ?? [],
			}, cancellationToken);

			TempData["SuccessMessage"] = "Scores saved and division standings refreshed.";
		}
		catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
		{
			TempData["ErrorMessage"] = ex.Message;
		}

		return RedirectToAction(nameof(Entry), new
		{
			year = form.Year,
			division = form.DivisionKey,
			round = form.SelectedRound,
			tab = form.ActiveTab,
		});
	}

	[HttpPost("/Scores/UpsertKftm")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> UpsertKftm(
		int year,
		string divisionKey,
		int divisionId,
		TiebreakerScope scope,
		int teamAId,
		int teamBId,
		int winningTeamId,
		CancellationToken cancellationToken = default)
	{
		try
		{
			await _scoreEntryService.UpsertKftmOverrideAsync(new KftmOverrideUpsertDto
			{
				DivisionId = divisionId,
				Scope = scope,
				TeamAId = teamAId,
				TeamBId = teamBId,
				WinningTeamId = winningTeamId,
			}, cancellationToken);

			TempData["SuccessMessage"] = $"Saved {scope} KFTM tiebreaker result.";
		}
		catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
		{
			TempData["ErrorMessage"] = ex.Message;
		}

		return RedirectToAction(nameof(Entry), new
		{
			year,
			division = divisionKey,
			tab = "kftm",
		});
	}

	[HttpPost("/Scores/RemoveKftm")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> RemoveKftm(
		int year,
		string divisionKey,
		int overrideId,
		CancellationToken cancellationToken = default)
	{
		try
		{
			await _scoreEntryService.RemoveKftmOverrideAsync(overrideId, cancellationToken);
			TempData["SuccessMessage"] = "Removed KFTM tiebreaker override.";
		}
		catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
		{
			TempData["ErrorMessage"] = ex.Message;
		}

		return RedirectToAction(nameof(Entry), new
		{
			year,
			division = divisionKey,
			tab = "kftm",
		});
	}

	private async Task<ScoresEntryPageViewModel> BuildPageViewModelAsync(
		TournamentYearDto selectedYear,
		List<TournamentYearDto> allYears,
		DivisionSummaryDto selectedDivision,
		List<DivisionSummaryDto> divisions,
		int? selectedRound,
		string? tab,
		CancellationToken cancellationToken)
	{
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

		var psoByPoolGameId = shootoutRounds
			.Where(r => r.PoolPlayGameId.HasValue)
			.GroupBy(r => r.PoolPlayGameId!.Value)
			.ToDictionary(g => g.Key, g => g.First());

		var psoByChampGameId = shootoutRounds
			.Where(r => r.ChampionshipGameId.HasValue)
			.GroupBy(r => r.ChampionshipGameId!.Value)
			.ToDictionary(g => g.Key, g => g.First());

		var allPoolGames = _dataContext.PoolPlayGames
			.Where(g => g.DivisionId == selectedDivision.Id)
			.OrderBy(g => g.Round)
			.ThenBy(g => g.ScheduledDateTime ?? DateTime.MaxValue)
			.ThenBy(g => g.Location)
			.ToList();

		var availableRounds = allPoolGames
			.Select(g => g.Round)
			.Distinct()
			.OrderBy(r => r)
			.ToList();

		if (selectedRound.HasValue && !availableRounds.Contains(selectedRound.Value))
		{
			selectedRound = null;
		}

		var filteredPoolGames = selectedRound.HasValue
			? allPoolGames.Where(g => g.Round == selectedRound.Value).ToList()
			: allPoolGames;

		var poolCards = filteredPoolGames
			.Select(g =>
			{
				teamsById.TryGetValue(g.HomeTeamId, out var home);
				teamsById.TryGetValue(g.AwayTeamId, out var away);
				psoByPoolGameId.TryGetValue(g.Id, out var pso);

				return new PoolPlayScoreEntryCardViewModel
				{
					GameId = g.Id,
					Round = g.Round,
					PoolName = poolsById.GetValueOrDefault(g.PoolId, string.Empty),
					ScheduledDateTime = g.ScheduledDateTime,
					Location = g.Location,
					HomeCode = home?.Code ?? string.Empty,
					HomeFullName = FormatFullTeam(home),
					AwayCode = away?.Code ?? string.Empty,
					AwayFullName = FormatFullTeam(away),
					HomeGoals = g.HomeGoals,
					AwayGoals = g.AwayGoals,
					IsForfeit = g.IsForfeit,
					HomeYellowCards = g.HomeYellowCards,
					HomeRedCards = g.HomeRedCards,
					AwayYellowCards = g.AwayYellowCards,
					AwayRedCards = g.AwayRedCards,
					PairedShootoutRoundId = pso?.Id,
					PairedShootoutRoundNumber = pso?.Round,
					HomePkGoals = pso?.HomePkGoals,
					AwayPkGoals = pso?.AwayPkGoals,
					ModifiedAt = g.ModifiedAt,
					ModifiedBy = g.ModifiedBy,
				};
			})
			.ToList();

		var champEntitiesById = _dataContext.ChampionshipGames
			.Where(g => g.DivisionId == selectedDivision.Id)
			.ToDictionary(g => g.Id);

		var champCards = bracketResult.Games
			.Where(g => g.GameId.HasValue)
			.Select(g =>
			{
				var id = g.GameId!.Value;
				var home = g.HomeTeamId.HasValue ? teamsById.GetValueOrDefault(g.HomeTeamId.Value) : null;
				var away = g.AwayTeamId.HasValue ? teamsById.GetValueOrDefault(g.AwayTeamId.Value) : null;
				psoByChampGameId.TryGetValue(id, out var r4Pso);
				champEntitiesById.TryGetValue(id, out var entity);

				return new ChampionshipScoreEntryCardViewModel
				{
					GameId = id,
					Slot = g.Slot,
					SlotLabel = ImportController.FormatBracketSlotLabel(g.Slot),
					ScheduledDateTime = g.ScheduledDateTime,
					Location = g.Location,
					IsSeeded = home is not null && away is not null,
					HomeDisplay = home is not null ? FormatFullTeam(home) : g.HomeSourceDescription,
					AwayDisplay = away is not null ? FormatFullTeam(away) : g.AwaySourceDescription,
					HomeGoals = entity?.HomeGoals ?? g.HomeGoals,
					AwayGoals = entity?.AwayGoals ?? g.AwayGoals,
					IsForfeit = entity?.IsForfeit ?? g.IsForfeit,
					PairedRound4ShootoutId = r4Pso?.Id,
					HomePkGoals = r4Pso?.HomePkGoals,
					AwayPkGoals = r4Pso?.AwayPkGoals,
				};
			})
			.ToList();

		var activePrompts = BuildActiveKftmPrompts(poolStandings, psoResult, teamsById);
		var savedOverrides = _dataContext.TiebreakerOverrides
			.Where(o => o.DivisionId == selectedDivision.Id)
			.OrderBy(o => o.Scope)
			.ThenByDescending(o => o.ModifiedAt)
			.Select(o => new SavedKftmOverrideViewModel
			{
				Id = o.Id,
				Scope = o.Scope,
				TeamADisplay = FormatFullTeam(teamsById.GetValueOrDefault(o.TeamAId)),
				TeamBDisplay = FormatFullTeam(teamsById.GetValueOrDefault(o.TeamBId)),
				WinnerDisplay = FormatFullTeam(teamsById.GetValueOrDefault(o.WinningTeamId)),
				ModifiedAt = o.ModifiedAt,
				ModifiedBy = o.ModifiedBy,
			})
			.ToList();

		var divisionPoolIds = poolsById.Keys.ToHashSet();
		var divisionTeamOptions = _dataContext.Teams
			.Where(t => divisionPoolIds.Contains(t.PoolId))
			.OrderBy(t => t.Code)
			.Select(t => new SelectListItem
			{
				Value = t.Id.ToString(),
				Text = FormatFullTeam(t),
			})
			.ToList();

		var hasBracket = DivisionSizeRules.HasChampionshipBracket(selectedDivision.Size);
		var normalizedTab = (tab ?? "pool").Trim().ToLowerInvariant();
		if (normalizedTab == "bracket" && !hasBracket)
		{
			normalizedTab = "pool";
		}
		else if (normalizedTab is not ("pool" or "bracket" or "kftm"))
		{
			normalizedTab = "pool";
		}

		return new ScoresEntryPageViewModel
		{
			SelectedYear = selectedYear,
			AvailableYears = allYears,
			SelectedDivision = selectedDivision,
			Divisions = divisions,
			SelectedRound = selectedRound,
			AvailableRounds = availableRounds,
			ActiveTab = normalizedTab,
			HasChampionshipBracket = hasBracket,
			PoolPlayCards = poolCards,
			ChampionshipCards = champCards,
			ActiveKftmPrompts = activePrompts,
			SavedKftmOverrides = savedOverrides,
			DivisionTeamOptions = divisionTeamOptions,
		};
	}

	private static List<ActiveKftmTiePromptViewModel> BuildActiveKftmPrompts(
		DivisionPoolPlayStandingsResult poolStandings,
		PsoStandingsResult psoResult,
		IReadOnlyDictionary<int, Team> teamsById)
	{
		var prompts = new List<ActiveKftmTiePromptViewModel>();

		void AddPoolPromptIfNeeded(PoolPlayStandingsResult? res, string label)
		{
			if (res is not { RequiresKftmTiebreaker: true, TiedTeamIdsForKftm.Count: >= 2 })
			{
				return;
			}

			prompts.Add(new ActiveKftmTiePromptViewModel
			{
				Scope = TiebreakerScope.PoolPlayStandings,
				ContextLabel = label,
				TiedTeams = res.TiedTeamIdsForKftm
					.Select(id => new SelectListItem
					{
						Value = id.ToString(),
						Text = FormatFullTeam(teamsById.GetValueOrDefault(id)),
					})
					.ToList(),
			});
		}

		foreach (var pool in poolStandings.Pools)
		{
			AddPoolPromptIfNeeded(pool, $"Pool {pool.PoolName} Standings");
		}

		AddPoolPromptIfNeeded(poolStandings.CombinedDivisionStandings, "Combined Division Standings");
		AddPoolPromptIfNeeded(poolStandings.PoolWinnersStandings, "12-Team Pool Winners Sub-Ranking");
		AddPoolPromptIfNeeded(poolStandings.RunnersUpStandings, "12-Team Runners-Up Wildcard Sub-Ranking");

		if (psoResult is { RequiresKftmTiebreaker: true, TiedTeamIdsForKftm.Count: >= 2 })
		{
			prompts.Add(new ActiveKftmTiePromptViewModel
			{
				Scope = TiebreakerScope.PsoStandings,
				ContextLabel = "Division Penalty Shootout (PSO) Standings",
				TiedTeams = psoResult.TiedTeamIdsForKftm
					.Select(id => new SelectListItem
					{
						Value = id.ToString(),
						Text = FormatFullTeam(teamsById.GetValueOrDefault(id)),
					})
					.ToList(),
			});
		}

		return prompts;
	}

	private static string FormatFullTeam(Team? team)
	{
		if (team is null)
		{
			return "Unknown Team";
		}

		return string.IsNullOrWhiteSpace(team.Code) ? team.Name : $"{team.Code} {team.Name}";
	}
}
