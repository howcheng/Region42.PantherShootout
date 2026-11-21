using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Helpers;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Interfaces;
using Region42.PantherShootout.Web.Helpers;
using Region42.PantherShootout.Web.Models;

namespace Region42.PantherShootout.Web.Controllers;

public class HomeController : Controller
{
	private readonly ITournamentYearService _tournamentYearService;
	private readonly ITournamentDataContext _dataContext;

	public HomeController(
		ITournamentYearService tournamentYearService,
		ITournamentDataContext dataContext)
	{
		_tournamentYearService = tournamentYearService ?? throw new ArgumentNullException(nameof(tournamentYearService));
		_dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
	}

	[AllowAnonymous]
	public async Task<IActionResult> Index(int? year = null, CancellationToken cancellationToken = default)
	{
		var allYears = await _tournamentYearService.GetAllYearsAsync(cancellationToken);
		TournamentYearDto? selectedYear = null;

		if (year.HasValue)
		{
			selectedYear = allYears.FirstOrDefault(y => y.Year == year.Value);
		}

		selectedYear ??= await _tournamentYearService.GetDefaultYearAsync(cancellationToken);

		var preferredKey = DivisionPreferenceCookieHelper.GetPreferredDivisionKey(HttpContext);

		if (selectedYear is null)
		{
			return View(new HomeLandingViewModel
			{
				AvailableYears = allYears.ToList(),
				PreferredDivisionKey = preferredKey,
			});
		}

		var divisionSummaries = await _tournamentYearService.GetDivisionsForYearAsync(selectedYear.Id, cancellationToken);
		var cards = new List<DivisionHubCardViewModel>(divisionSummaries.Count);

		foreach (var summary in divisionSummaries)
		{
			await _dataContext.LoadDivisionDataAsync(summary.Id, cancellationToken);
			cards.Add(BuildDivisionCard(summary, preferredKey));
		}

		return View(new HomeLandingViewModel
		{
			SelectedYear = selectedYear,
			AvailableYears = allYears.ToList(),
			PreferredDivisionKey = preferredKey,
			Divisions = cards,
		});
	}

	[AllowAnonymous]
	public IActionResult Privacy()
	{
		return View();
	}

	[AllowAnonymous]
	[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
	public IActionResult Error()
	{
		return View(new ErrorViewModel
		{
			RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
		});
	}

	private DivisionHubCardViewModel BuildDivisionCard(DivisionSummaryDto summary, string? preferredKey)
	{
		var teamsById = _dataContext.Teams.ToDictionary(t => t.Id);
		var poolGames = _dataContext.PoolPlayGames.Where(g => g.DivisionId == summary.Id).ToList();
		var champGames = _dataContext.ChampionshipGames.Where(g => g.DivisionId == summary.Id).ToList();
		var completedPoolGames = poolGames.Count(g => g.HomeGoals.HasValue && g.AwayGoals.HasValue);
		var completedChampGames = champGames.Count(g => g.HomeGoals.HasValue && g.AwayGoals.HasValue);

		var snapshot = _dataContext.GetStandingsForDivision(summary.Id);
		var hasBracket = DivisionSizeRules.HasChampionshipBracket(summary.Size);

		string? championDisplay = null;
		if (snapshot?.ChampionTeamId is int champId && teamsById.TryGetValue(champId, out var champTeam))
		{
			championDisplay = FormatTeamShortDisplay(champTeam);
		}

		string? psoLeaderDisplay = null;
		var isPsoFinal = false;
		if (snapshot?.PsoStandings is { Teams.Count: > 0 } pso)
		{
			isPsoFinal = pso.IsFinal;
			if (pso.WinnerTeamId is int psoWinnerId && teamsById.TryGetValue(psoWinnerId, out var psoWinner))
			{
				psoLeaderDisplay = FormatTeamShortDisplay(psoWinner);
			}
			else if (pso.Teams.Any(t => t.RoundsCompleted > 0))
			{
				var leaderId = pso.Teams.OrderBy(t => t.Rank).First().TeamId;
				if (teamsById.TryGetValue(leaderId, out var leaderTeam))
				{
					psoLeaderDisplay = FormatTeamShortDisplay(leaderTeam);
				}
			}
		}

		var allPoolsFinal = snapshot is not null &&
			snapshot.Pools.Count > 0 &&
			snapshot.Pools.All(p => p.IsFinal);

		string phaseLabel;
		string badgeClass;

		if (championDisplay is not null)
		{
			phaseLabel = "Complete";
			badgeClass = "bg-success text-white";
		}
		else if (hasBracket && (completedChampGames > 0 || allPoolsFinal))
		{
			phaseLabel = "Championships";
			badgeClass = "bg-primary text-white";
		}
		else if (completedPoolGames > 0)
		{
			phaseLabel = "Pool Play";
			badgeClass = "bg-info-subtle text-info-emphasis";
		}
		else
		{
			phaseLabel = "Scheduled";
			badgeClass = "bg-secondary-subtle text-secondary-emphasis";
		}

		return new DivisionHubCardViewModel
		{
			DivisionId = summary.Id,
			DivisionKey = summary.DivisionKey,
			ShortCode = summary.ShortCode,
			Name = summary.Name,
			AgeGroup = summary.AgeGroup,
			Gender = summary.Gender,
			Size = summary.Size,
			PoolCount = summary.PoolCount,
			TeamCount = summary.TeamCount,
			CompletedPoolGames = completedPoolGames,
			TotalPoolGames = poolGames.Count,
			HasBracket = hasBracket,
			PhaseLabel = phaseLabel,
			PhaseBadgeClass = badgeClass,
			ChampionTeamDisplay = championDisplay,
			PsoLeaderDisplay = psoLeaderDisplay,
			IsPsoFinal = isPsoFinal,
			IsPreferred = string.Equals(summary.DivisionKey, preferredKey, StringComparison.OrdinalIgnoreCase),
			CustomMessage = summary.CustomMessage,
		};
	}

	private static string FormatTeamShortDisplay(Team team)
	{
		var code = team.Code;
		return string.IsNullOrWhiteSpace(code) ? team.Name : $"{code} {team.Name}";
	}
}
