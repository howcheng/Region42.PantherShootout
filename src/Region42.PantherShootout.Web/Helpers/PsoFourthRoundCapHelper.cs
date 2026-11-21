using Region42.PantherShootout.Application.Helpers;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;

namespace Region42.PantherShootout.Web.Helpers;

/// <summary>
/// Identifies teams in 4-round PSO divisions (<c>5</c>, <c>6</c>, and <c>10</c> teams) whose
/// Round 4 shootout score does not count because they already have 3 shootout turns in Rounds 1–3.
/// </summary>
public static class PsoFourthRoundCapHelper
{
	public static PsoFourthRoundCapAnalysis Analyze(
		DivisionSize size,
		IReadOnlyList<ShootoutRound> shootoutRounds)
	{
		ArgumentNullException.ThrowIfNull(shootoutRounds);

		var hasFourthRound = DivisionSizeRules.HasFourthShootoutRound(size);
		if (!hasFourthRound)
		{
			return new PsoFourthRoundCapAnalysis(
				HasFourthShootoutRound: false,
				NonCountingFourthRoundTeamIds: [],
				ExcludedFourthRoundScoresByTeamId: []);
		}

		var nonCountingTeamIds = new HashSet<int>();
		var excludedScoresByTeamId = new Dictionary<int, int>();

		var round4Matchups = shootoutRounds.Where(r => r.Round == 4).ToList();
		var candidateTeamIds = round4Matchups
			.SelectMany(r => new[] { r.HomeTeamId, r.AwayTeamId })
			.Where(id => id.HasValue)
			.Select(id => id!.Value)
			.Distinct();

		foreach (var teamId in candidateTeamIds)
		{
			var scheduledEarlyTurns = shootoutRounds.Count(r =>
				r.Round <= 3 &&
				(r.HomeTeamId == teamId || r.AwayTeamId == teamId));

			var completedEarlyTurns = shootoutRounds.Count(r =>
				r.Round <= 3 &&
				r.IsComplete &&
				(r.HomeTeamId == teamId || r.AwayTeamId == teamId));

			if (scheduledEarlyTurns >= DivisionSizeRules.RequiredPsoMatchupsPerTeam)
			{
				nonCountingTeamIds.Add(teamId);
			}

			if (completedEarlyTurns >= DivisionSizeRules.RequiredPsoMatchupsPerTeam)
			{
				var completedRound4 = round4Matchups.FirstOrDefault(r =>
					r.IsComplete &&
					(r.HomeTeamId == teamId || r.AwayTeamId == teamId));

				if (completedRound4 is not null)
				{
					var excludedPk = completedRound4.HomeTeamId == teamId
						? completedRound4.HomePkGoals!.Value
						: completedRound4.AwayPkGoals!.Value;
					excludedScoresByTeamId[teamId] = excludedPk;
				}
			}
		}

		return new PsoFourthRoundCapAnalysis(
			HasFourthShootoutRound: true,
			NonCountingFourthRoundTeamIds: nonCountingTeamIds,
			ExcludedFourthRoundScoresByTeamId: excludedScoresByTeamId);
	}
}

public sealed record PsoFourthRoundCapAnalysis(
	bool HasFourthShootoutRound,
	HashSet<int> NonCountingFourthRoundTeamIds,
	Dictionary<int, int> ExcludedFourthRoundScoresByTeamId)
{
	public bool IsTeamExcludedInRound(int roundNumber, int? teamId) =>
		HasFourthShootoutRound &&
		roundNumber == 4 &&
		teamId.HasValue &&
		NonCountingFourthRoundTeamIds.Contains(teamId.Value);
}
