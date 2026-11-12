using System.Globalization;
using System.Text.RegularExpressions;
using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Exceptions;
using Region42.PantherShootout.Application.Helpers;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Domain;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;
using Region42.PantherShootout.Domain.Helpers;
using Region42.PantherShootout.Domain.Interfaces;
using Region42.PantherShootout.Domain.Models;

namespace Region42.PantherShootout.Application.Services;

/// <summary>
/// Validates and imports tournament divisions, pools, discrete team fields, pool-play schedules,
/// championship bracket game times/locations, and PSO shootout rounds from an <c>.xlsx</c> workbook.
/// </summary>
public class ScheduleImportService : IScheduleImportService
{
	public const int MaxColorChannelTolerance = 12;

	private static readonly TimeSpan WriteLockTtl = TimeSpan.FromMinutes(2);

	private static readonly Regex TeamCodeRegex = new(
		@"^(?<pool>[A-Za-z])(?<num>[1-9]\d*)$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex TeamTokenRegex = new(
		@"^(?<div>[A-Za-z0-9]+)-(?<region>\d+)-(?<coach>.+)$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex PoolPlayMatchupRegex = new(
		@"^(?<home>[A-Za-z][1-9]\d*)\s+(?:vs\.?|v\.?)\s+(?<away>[A-Za-z][1-9]\d*)$",
		RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	private static readonly Regex VersusNormalizeRegex = new(
		@"\s+(?:vs\.?|v\.?)\s+",
		RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	private static readonly Regex WhitespaceCollapseRegex = new(
		@"\s+",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex UppercaseSingleLetterRegex = new(
		@"\b(?<letter>[A-Z])\b",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex SemifinalSeedForwardRegex = new(
		@"(?:1st\s+(?<firstPool>[a-z0-9 ]+?)|(?<firstPool>[a-z0-9 ]+?)\s+seed\s*1)\s+vs\s+(?:2nd\s+(?<secondPool>[a-z0-9 ]+?)|(?<secondPool>[a-z0-9 ]+?)\s+seed\s*2)$",
		RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	private static readonly Regex SemifinalSeedReverseRegex = new(
		@"(?:2nd\s+(?<secondPool>[a-z0-9 ]+?)|(?<secondPool>[a-z0-9 ]+?)\s+seed\s*2)\s+vs\s+(?:1st\s+(?<firstPool>[a-z0-9 ]+?)|(?<firstPool>[a-z0-9 ]+?)\s+seed\s*1)$",
		RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	private static readonly Regex BothSidesFirstSeedRegex = new(
		@"(?:1st\b|seed\s*1\b).*\bvs\b.*(?:1st\b|seed\s*1\b)",
		RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	private static readonly Regex BothSidesSecondSeedRegex = new(
		@"(?:2nd\b|seed\s*2\b).*\bvs\b.*(?:2nd\b|seed\s*2\b)",
		RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	private readonly IScheduleWorkbookReader _workbookReader;
	private readonly ITournamentDataContext _dataContext;
	private readonly IStorageWriteLock _writeLock;
	private readonly IStandingsRefreshService _standingsRefreshService;
	private readonly ICurrentUserService _currentUserService;

	public ScheduleImportService(
		IScheduleWorkbookReader workbookReader,
		ITournamentDataContext dataContext,
		IStorageWriteLock writeLock,
		IStandingsRefreshService standingsRefreshService,
		ICurrentUserService currentUserService)
	{
		_workbookReader = workbookReader ?? throw new ArgumentNullException(nameof(workbookReader));
		_dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
		_writeLock = writeLock ?? throw new ArgumentNullException(nameof(writeLock));
		_standingsRefreshService = standingsRefreshService ?? throw new ArgumentNullException(nameof(standingsRefreshService));
		_currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
	}

	public Task<ScheduleImportPreviewResult> ValidateWorkbookAsync(
		Stream workbookStream,
		int tournamentYearId,
		IReadOnlyList<ScheduleCellOverrideDto>? cellOverrides = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(workbookStream);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tournamentYearId);

		return ValidateWorkbookInternalAsync(workbookStream, tournamentYearId, cellOverrides, cancellationToken);
	}

	private async Task<ScheduleImportPreviewResult> ValidateWorkbookInternalAsync(
		Stream workbookStream,
		int tournamentYearId,
		IReadOnlyList<ScheduleCellOverrideDto>? cellOverrides,
		CancellationToken cancellationToken)
	{
		await _dataContext.LoadAllAsync(cancellationToken).ConfigureAwait(false);

		var tournamentYear = _dataContext.TournamentYears.FirstOrDefault(y => y.Id == tournamentYearId)
			?? throw new InvalidOperationException($"Tournament year {tournamentYearId} was not found.");

		await LoadExistingDivisionsForYearAsync(tournamentYearId, cancellationToken).ConfigureAwait(false);

		await using var buffered = await BufferStreamAsync(workbookStream, cancellationToken).ConfigureAwait(false);
		var rawWorkbook = _workbookReader.ReadWorkbook(buffered);

		return BuildPreviewPlan(rawWorkbook, tournamentYear, cellOverrides);
	}

	public Task<ScheduleImportCommitResult> ImportWorkbookAsync(
		Stream workbookStream,
		int tournamentYearId,
		bool allowOverwriteScores = false,
		IReadOnlyList<ScheduleCellOverrideDto>? cellOverrides = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(workbookStream);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tournamentYearId);

		return ImportWorkbookInternalAsync(
			workbookStream,
			tournamentYearId,
			allowOverwriteScores,
			cellOverrides,
			cancellationToken);
	}

	private async Task<ScheduleImportCommitResult> ImportWorkbookInternalAsync(
		Stream workbookStream,
		int tournamentYearId,
		bool allowOverwriteScores,
		IReadOnlyList<ScheduleCellOverrideDto>? cellOverrides,
		CancellationToken cancellationToken)
	{
		await using var buffered = await BufferStreamAsync(workbookStream, cancellationToken).ConfigureAwait(false);

		var holder = string.IsNullOrWhiteSpace(_currentUserService.UserIdentifier)
			? "System"
			: _currentUserService.UserIdentifier;

		await using var lockHandle = await _writeLock
			.TryAcquireAsync(StorageWriteLockNames.ScheduleImport, holder, WriteLockTtl, cancellationToken)
			.ConfigureAwait(false);

		if (lockHandle is null)
		{
			throw new InvalidOperationException(
				"Another schedule import is currently in progress. Please wait and try again.");
		}

		await _dataContext.LoadAllAsync(cancellationToken).ConfigureAwait(false);

		var tournamentYear = _dataContext.TournamentYears.FirstOrDefault(y => y.Id == tournamentYearId)
			?? throw new InvalidOperationException($"Tournament year {tournamentYearId} was not found.");

		await LoadExistingDivisionsForYearAsync(tournamentYearId, cancellationToken).ConfigureAwait(false);

		var rawWorkbook = _workbookReader.ReadWorkbook(buffered);
		var preview = BuildPreviewPlan(rawWorkbook, tournamentYear, cellOverrides);

		if (!preview.IsValid)
		{
			throw new ScheduleImportValidationException(preview.Errors);
		}

		if (preview.HasExistingScores && !allowOverwriteScores)
		{
			throw new InvalidOperationException(
				"Cannot overwrite existing tournament schedule because game or shootout scores have already been recorded. Confirm score overwrite to proceed.");
		}

		await using var transaction = await _dataContext.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

		var existingDivisions = _dataContext.Divisions
			.Where(d => d.TournamentYearId == tournamentYearId)
			.ToList();

		foreach (var existingDiv in existingDivisions)
		{
			_dataContext.Remove(existingDiv);
		}

		var poolsImported = 0;
		var teamsImported = 0;
		var poolGamesImported = 0;
		var champGamesImported = 0;
		var shootoutRoundsImported = 0;

		var createdDivisionIds = new List<int>(preview.Divisions.Count);

		foreach (var divPlan in preview.Divisions)
		{
			var division = new Division
			{
				Id = _dataContext.AllocateNextId(),
				TournamentYearId = tournamentYearId,
				AgeGroup = divPlan.AgeGroup,
				Gender = divPlan.Gender,
				Name = divPlan.DivisionName,
				Size = divPlan.Size,
			};
			_dataContext.Add(division);
			createdDivisionIds.Add(division.Id);

			var poolsByName = new Dictionary<string, Pool>(StringComparer.OrdinalIgnoreCase);
			var teamsByCode = new Dictionary<string, Team>(StringComparer.OrdinalIgnoreCase);

			foreach (var poolPlan in divPlan.Pools)
			{
				var pool = new Pool
				{
					Id = _dataContext.AllocateNextId(),
					DivisionId = division.Id,
					Name = poolPlan.PoolName,
				};
				_dataContext.Add(pool);
				poolsByName[pool.Name] = pool;
				poolsImported++;

				foreach (var teamPlan in poolPlan.Teams)
				{
					var team = new Team
					{
						Id = _dataContext.AllocateNextId(),
						PoolId = pool.Id,
						TeamNumber = teamPlan.TeamNumber,
						RegionNumber = teamPlan.RegionNumber,
						RegionName = teamPlan.RegionName,
						CoachName = teamPlan.CoachName,
					};
					_dataContext.Add(team);
					teamsByCode[teamPlan.Code] = team;
					teamsImported++;
				}
			}

			var maxPsoRoundForPoolPlay = divPlan.Size == DivisionSize.Six
				? 3
				: DivisionSizeRules.GetShootoutRoundCount(divPlan.Size);

			foreach (var gamePlan in divPlan.PoolPlayGames)
			{
				var pool = poolsByName[gamePlan.PoolName];
				var homeTeam = teamsByCode[gamePlan.HomeTeamCode];
				var awayTeam = teamsByCode[gamePlan.AwayTeamCode];

				var poolGame = new PoolPlayGame
				{
					Id = _dataContext.AllocateNextId(),
					DivisionId = division.Id,
					PoolId = pool.Id,
					Round = gamePlan.Round,
					ScheduledDateTime = gamePlan.ScheduledDateTime,
					Location = gamePlan.Location,
					HomeTeamId = homeTeam.Id,
					AwayTeamId = awayTeam.Id,
				};
				_dataContext.Add(poolGame);
				poolGamesImported++;

				if (poolGame.Round <= maxPsoRoundForPoolPlay)
				{
					var shootoutRound = new ShootoutRound
					{
						Id = _dataContext.AllocateNextId(),
						DivisionId = division.Id,
						Round = poolGame.Round,
						PoolPlayGameId = poolGame.Id,
						HomeTeamId = homeTeam.Id,
						AwayTeamId = awayTeam.Id,
					};
					_dataContext.Add(shootoutRound);
					shootoutRoundsImported++;
				}
			}

			var createdChampGames = new List<ChampionshipGame>(divPlan.ChampionshipGames.Count);
			foreach (var champPlan in divPlan.ChampionshipGames)
			{
				var champGame = new ChampionshipGame
				{
					Id = _dataContext.AllocateNextId(),
					DivisionId = division.Id,
					Slot = champPlan.Slot,
					ScheduledDateTime = champPlan.ScheduledDateTime,
					Location = champPlan.Location,
					HomeSourceDescription = champPlan.HomeSourceDescription,
					AwaySourceDescription = champPlan.AwaySourceDescription,
				};
				_dataContext.Add(champGame);
				createdChampGames.Add(champGame);
				champGamesImported++;
			}

			if (divPlan.Size == DivisionSize.Six)
			{
				foreach (var openingChampGame in createdChampGames.Where(g =>
					g.Slot is BracketSlot.Consolation or BracketSlot.Semifinal1 or BracketSlot.Semifinal2))
				{
					var round4 = new ShootoutRound
					{
						Id = _dataContext.AllocateNextId(),
						DivisionId = division.Id,
						Round = ChampionshipBracketBuilder.PsoRound4,
						ChampionshipGameId = openingChampGame.Id,
					};
					_dataContext.Add(round4);
					shootoutRoundsImported++;
				}
			}
		}

		foreach (var divisionId in createdDivisionIds)
		{
			await _standingsRefreshService
				.RefreshDivisionAsync(divisionId, saveChanges: false, cancellationToken)
				.ConfigureAwait(false);
		}

		await _dataContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

		return new ScheduleImportCommitResult
		{
			TournamentYearId = tournamentYearId,
			DivisionsImported = preview.Divisions.Count,
			PoolsImported = poolsImported,
			TeamsImported = teamsImported,
			PoolPlayGamesImported = poolGamesImported,
			ChampionshipGamesImported = champGamesImported,
			ShootoutRoundsImported = shootoutRoundsImported,
		};
	}

	private async Task LoadExistingDivisionsForYearAsync(int tournamentYearId, CancellationToken cancellationToken)
	{
		var existingDivisions = _dataContext.Divisions
			.Where(d => d.TournamentYearId == tournamentYearId)
			.ToList();

		foreach (var division in existingDivisions)
		{
			await _dataContext.LoadDivisionDataAsync(division.Id, cancellationToken).ConfigureAwait(false);
		}
	}

	private static async Task<MemoryStream> BufferStreamAsync(Stream input, CancellationToken cancellationToken)
	{
		if (input.CanSeek)
		{
			input.Position = 0;
		}

		var buffer = new MemoryStream();
		await input.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
		buffer.Position = 0;
		return buffer;
	}

	private ScheduleImportPreviewResult BuildPreviewPlan(
		ParsedWorkbookData workbook,
		TournamentYear tournamentYear,
		IReadOnlyList<ScheduleCellOverrideDto>? cellOverrides)
	{
		var errors = new List<ScheduleImportValidationErrorDto>();
		var missingRegions = new SortedSet<int>();

		foreach (var issue in workbook.ReaderIssues)
		{
			errors.Add(new ScheduleImportValidationErrorDto
			{
				Code = "WorkbookFormatError",
				SheetName = issue.SheetName,
				CellReference = issue.CellReference,
				Message = issue.Message,
			});
		}

		if (!workbook.HasPoolsSheet)
		{
			errors.Add(new ScheduleImportValidationErrorDto
			{
				Code = "MissingSheet",
				SheetName = "Pools",
				CellReference = string.Empty,
				Message = "Workbook is missing the required 'Pools' worksheet.",
			});
		}

		if (!workbook.HasScheduleSheet)
		{
			errors.Add(new ScheduleImportValidationErrorDto
			{
				Code = "MissingSheet",
				SheetName = "Schedule",
				CellReference = string.Empty,
				Message = "Workbook is missing the required 'Schedule' worksheet.",
			});
		}

		var existingDivisionIds = _dataContext.Divisions
			.Where(d => d.TournamentYearId == tournamentYear.Id)
			.Select(d => d.Id)
			.ToHashSet();

		var hasExistingData = existingDivisionIds.Count > 0;
		var hasExistingScores = existingDivisionIds.Count > 0 && (
			_dataContext.PoolPlayGames.Any(g => existingDivisionIds.Contains(g.DivisionId) && (g.HomeGoals is not null || g.AwayGoals is not null || g.IsForfeit)) ||
			_dataContext.ChampionshipGames.Any(g => existingDivisionIds.Contains(g.DivisionId) && (g.HomeGoals is not null || g.AwayGoals is not null || g.IsForfeit)) ||
			_dataContext.ShootoutRounds.Any(r => existingDivisionIds.Contains(r.DivisionId) && (r.HomePkGoals is not null || r.AwayPkGoals is not null)));

		var regionsByNumber = _dataContext.Regions
			.GroupBy(r => r.RegionNumber)
			.ToDictionary(g => g.Key, g => g.First().Name);

		var divisionsByKey = new Dictionary<string, DivisionBuildState>(StringComparer.OrdinalIgnoreCase);
		var teamsByCode = new Dictionary<string, TeamBuildState>(StringComparer.OrdinalIgnoreCase);
		var poolNameToDivisionKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		if (workbook.HasPoolsSheet && workbook.DivisionBlocks.Count == 0)
		{
			errors.Add(new ScheduleImportValidationErrorDto
			{
				Code = "EmptyPoolsSheet",
				SheetName = "Pools",
				CellReference = string.Empty,
				Message = "No division blocks were found on the 'Pools' worksheet.",
			});
		}

		ParseAndValidatePools(
			workbook,
			regionsByNumber,
			divisionsByKey,
			teamsByCode,
			poolNameToDivisionKey,
			missingRegions,
			errors);

		var bracketMappings = new List<BracketCellMappingPreviewDto>();
		if (workbook.HasScheduleSheet && divisionsByKey.Count > 0)
		{
			ParseAndValidateSchedule(
				workbook,
				divisionsByKey,
				teamsByCode,
				poolNameToDivisionKey,
				cellOverrides,
				bracketMappings,
				errors);
		}

		var divisionPreviews = divisionsByKey.Values
			.OrderBy(d => d.AgeGroup)
			.ThenBy(d => d.Gender)
			.Select(BuildDivisionPreviewDto)
			.ToList();

		return new ScheduleImportPreviewResult
		{
			TournamentYearId = tournamentYear.Id,
			Year = tournamentYear.Year,
			HasExistingData = hasExistingData,
			HasExistingScores = hasExistingScores,
			MissingRegionNumbers = missingRegions.ToList(),
			Errors = errors,
			Divisions = divisionPreviews,
			BracketCellMappings = bracketMappings,
		};
	}

	private static void ParseAndValidatePools(
		ParsedWorkbookData workbook,
		IReadOnlyDictionary<int, string> regionsByNumber,
		Dictionary<string, DivisionBuildState> divisionsByKey,
		Dictionary<string, TeamBuildState> teamsByCode,
		Dictionary<string, string> poolNameToDivisionKey,
		SortedSet<int> missingRegions,
		List<ScheduleImportValidationErrorDto> errors)
	{
		foreach (var block in workbook.DivisionBlocks)
		{
			AgeGroup ageGroup;
			Gender gender;
			try
			{
				(ageGroup, gender) = DivisionKeyHelper.ParseDivisionName(block.DivisionHeaderText);
			}
			catch (ArgumentException)
			{
				errors.Add(new ScheduleImportValidationErrorDto
				{
					Code = "InvalidDivisionHeader",
					SheetName = "Pools",
					CellReference = block.HeaderCellReference,
					Message = $"Unrecognized division header '{block.DivisionHeaderText}'.",
				});
				continue;
			}

			var divisionKey = DivisionKeyHelper.ToKey(ageGroup, gender);
			var divisionName = DivisionKeyHelper.ToDisplayName(ageGroup, gender);

			if (divisionsByKey.ContainsKey(divisionKey))
			{
				errors.Add(new ScheduleImportValidationErrorDto
				{
					Code = "DuplicateDivision",
					SheetName = "Pools",
					CellReference = block.HeaderCellReference,
					Message = $"Division '{divisionName}' appears more than once on the 'Pools' worksheet.",
				});
				continue;
			}

			var divState = new DivisionBuildState
			{
				DivisionKey = divisionKey,
				DivisionName = divisionName,
				AgeGroup = ageGroup,
				Gender = gender,
				HeaderCellReference = block.HeaderCellReference,
				HeaderFillRgb = block.HeaderFillRgb,
			};

			foreach (var poolCol in block.Pools)
			{
				string? inferredPoolLetter = null;
				var poolTeams = new List<TeamBuildState>();
				var seenTeamNumbers = new HashSet<int>();

				foreach (var row in poolCol.Teams)
				{
					if (string.IsNullOrWhiteSpace(row.RawCode))
					{
						errors.Add(new ScheduleImportValidationErrorDto
						{
							Code = "MissingTeamCode",
							SheetName = "Pools",
							CellReference = row.CodeCellReference,
							Message = $"Missing team code for '{row.RawTeamToken}' in {divisionName}.",
						});
						continue;
					}

					var codeMatch = TeamCodeRegex.Match(row.RawCode);
					if (!codeMatch.Success)
					{
						errors.Add(new ScheduleImportValidationErrorDto
						{
							Code = "InvalidTeamCode",
							SheetName = "Pools",
							CellReference = row.CodeCellReference,
							Message = $"Invalid team code '{row.RawCode}'. Expected a pool letter followed by a 1-based number (e.g. 'A1').",
						});
						continue;
					}

					var poolLetter = codeMatch.Groups["pool"].Value.ToUpperInvariant();
					var teamNumber = int.Parse(codeMatch.Groups["num"].Value, CultureInfo.InvariantCulture);
					var normalizedCode = $"{poolLetter}{teamNumber}";

					if (inferredPoolLetter is null)
					{
						inferredPoolLetter = poolLetter;
					}
					else if (!string.Equals(inferredPoolLetter, poolLetter, StringComparison.OrdinalIgnoreCase))
					{
						errors.Add(new ScheduleImportValidationErrorDto
						{
							Code = "InconsistentPoolLetter",
							SheetName = "Pools",
							CellReference = row.CodeCellReference,
							Message = $"Team code '{normalizedCode}' uses pool '{poolLetter}', but earlier rows in the same column use pool '{inferredPoolLetter}'.",
						});
					}

					if (!seenTeamNumbers.Add(teamNumber))
					{
						errors.Add(new ScheduleImportValidationErrorDto
						{
							Code = "DuplicateTeamNumber",
							SheetName = "Pools",
							CellReference = row.CodeCellReference,
							Message = $"Duplicate team number {teamNumber} in pool '{poolLetter}'.",
						});
					}

					if (teamsByCode.TryGetValue(normalizedCode, out var existingTeam))
					{
						errors.Add(new ScheduleImportValidationErrorDto
						{
							Code = "DuplicateTeamCode",
							SheetName = "Pools",
							CellReference = row.CodeCellReference,
							Message = $"Team code '{normalizedCode}' is already assigned in {existingTeam.DivisionName}.",
						});
						continue;
					}

					if (string.IsNullOrWhiteSpace(row.RawTeamToken))
					{
						errors.Add(new ScheduleImportValidationErrorDto
						{
							Code = "MissingTeamToken",
							SheetName = "Pools",
							CellReference = row.TeamCellReference,
							Message = $"Missing team identifier for code '{normalizedCode}' in {divisionName}.",
						});
						continue;
					}

					var tokenMatch = TeamTokenRegex.Match(row.RawTeamToken);
					if (!tokenMatch.Success)
					{
						errors.Add(new ScheduleImportValidationErrorDto
						{
							Code = "InvalidTeamToken",
							SheetName = "Pools",
							CellReference = row.TeamCellReference,
							Message = $"Team '{row.RawTeamToken}' does not match expected '<DIV>-<REGION#>-<COACH>' format (e.g. '10UG-121-Wright').",
						});
						continue;
					}

					var tokenDivRaw = tokenMatch.Groups["div"].Value;
					var regionNumber = int.Parse(tokenMatch.Groups["region"].Value, CultureInfo.InvariantCulture);
					var coachName = tokenMatch.Groups["coach"].Value.Trim();

					try
					{
						var (tokenAge, tokenGender) = DivisionKeyHelper.ParseDivisionName(tokenDivRaw);
						if (tokenAge != ageGroup || tokenGender != gender)
						{
							errors.Add(new ScheduleImportValidationErrorDto
							{
								Code = "TeamDivisionMismatch",
								SheetName = "Pools",
								CellReference = row.TeamCellReference,
								Message = $"Team '{row.RawTeamToken}' specifies division '{tokenDivRaw}', which does not match block '{divisionName}'.",
							});
						}
					}
					catch (ArgumentException)
					{
						errors.Add(new ScheduleImportValidationErrorDto
						{
							Code = "InvalidTeamDivisionPrefix",
							SheetName = "Pools",
							CellReference = row.TeamCellReference,
							Message = $"Team '{row.RawTeamToken}' has unrecognized division prefix '{tokenDivRaw}'.",
						});
					}

					if (regionNumber <= 0)
					{
						errors.Add(new ScheduleImportValidationErrorDto
						{
							Code = "InvalidRegionNumber",
							SheetName = "Pools",
							CellReference = row.TeamCellReference,
							Message = $"Team '{row.RawTeamToken}' has invalid region number '{regionNumber}'.",
						});
					}
					else if (!regionsByNumber.TryGetValue(regionNumber, out var resolvedRegionName))
					{
						missingRegions.Add(regionNumber);
						errors.Add(new ScheduleImportValidationErrorDto
						{
							Code = "MissingRegion",
							SheetName = "Pools",
							CellReference = row.TeamCellReference,
							Message = $"AYSO Region {regionNumber} (referenced by '{row.RawTeamToken}') is not registered in Regions. Add Region {regionNumber} before importing.",
							MissingRegionNumber = regionNumber,
						});
						resolvedRegionName = $"Region {regionNumber}";

						var unmappedTeam = new TeamBuildState
						{
							Code = normalizedCode,
							PoolName = poolLetter,
							TeamNumber = teamNumber,
							RegionNumber = regionNumber,
							RegionName = resolvedRegionName,
							CoachName = coachName,
							DivisionKey = divisionKey,
							DivisionName = divisionName,
						};
						poolTeams.Add(unmappedTeam);
						teamsByCode[normalizedCode] = unmappedTeam;
						continue;
					}
					else
					{
						var mappedTeam = new TeamBuildState
						{
							Code = normalizedCode,
							PoolName = poolLetter,
							TeamNumber = teamNumber,
							RegionNumber = regionNumber,
							RegionName = resolvedRegionName,
							CoachName = coachName,
							DivisionKey = divisionKey,
							DivisionName = divisionName,
						};
						poolTeams.Add(mappedTeam);
						teamsByCode[normalizedCode] = mappedTeam;
					}
				}

				if (poolTeams.Count == 0 || inferredPoolLetter is null)
				{
					continue;
				}

				if (poolNameToDivisionKey.TryGetValue(inferredPoolLetter, out var otherDivKey) &&
					!string.Equals(otherDivKey, divisionKey, StringComparison.OrdinalIgnoreCase))
				{
					errors.Add(new ScheduleImportValidationErrorDto
					{
						Code = "DuplicatePoolLetter",
						SheetName = "Pools",
						CellReference = block.HeaderCellReference,
						Message = $"Pool letter '{inferredPoolLetter}' is used in multiple divisions.",
					});
				}
				else
				{
					poolNameToDivisionKey[inferredPoolLetter] = divisionKey;
				}

				// Verify 1..N contiguous numbering within the pool.
				var orderedTeams = poolTeams.OrderBy(t => t.TeamNumber).ToList();
				for (var expectedNum = 1; expectedNum <= orderedTeams.Count; expectedNum++)
				{
					if (orderedTeams.All(t => t.TeamNumber != expectedNum))
					{
						errors.Add(new ScheduleImportValidationErrorDto
						{
							Code = "NonSequentialTeamNumbers",
							SheetName = "Pools",
							CellReference = block.HeaderCellReference,
							Message = $"Pool '{inferredPoolLetter}' in {divisionName} is missing team number {expectedNum} (expected 1..{orderedTeams.Count}).",
						});
					}
				}

				divState.Pools.Add(new PoolBuildState
				{
					PoolName = inferredPoolLetter,
					Teams = orderedTeams,
				});
			}

			divState.Pools.Sort((a, b) => string.Compare(a.PoolName, b.PoolName, StringComparison.OrdinalIgnoreCase));

			var totalTeams = divState.Pools.Sum(p => p.Teams.Count);
			if (!DivisionSizeRules.TryFromTeamCount(totalTeams, out var divisionSize))
			{
				errors.Add(new ScheduleImportValidationErrorDto
				{
					Code = "InvalidDivisionSize",
					SheetName = "Pools",
					CellReference = block.HeaderCellReference,
					Message = $"Division '{divisionName}' has {totalTeams} team(s), which is not a supported size (must be 4, 5, 6, 8, 10, or 12).",
				});
			}
			else
			{
				divState.Size = divisionSize;
				divState.HasValidSize = true;

				var expectedPoolCount = DivisionSizeRules.GetPoolCount(divisionSize);
				var expectedTeamsPerPool = DivisionSizeRules.GetTeamsPerPool(divisionSize);

				if (divState.Pools.Count != expectedPoolCount)
				{
					errors.Add(new ScheduleImportValidationErrorDto
					{
						Code = "InvalidPoolCount",
						SheetName = "Pools",
						CellReference = block.HeaderCellReference,
						Message = $"Division '{divisionName}' ({totalTeams} teams) must have {expectedPoolCount} pool(s), but {divState.Pools.Count} pool(s) were found.",
					});
				}

				foreach (var p in divState.Pools.Where(p => p.Teams.Count != expectedTeamsPerPool))
				{
					errors.Add(new ScheduleImportValidationErrorDto
					{
						Code = "InvalidTeamsPerPool",
						SheetName = "Pools",
						CellReference = block.HeaderCellReference,
						Message = $"Pool '{p.PoolName}' in '{divisionName}' has {p.Teams.Count} team(s); expected {expectedTeamsPerPool}.",
					});
				}
			}

			divisionsByKey[divisionKey] = divState;
		}
	}

	private static void ParseAndValidateSchedule(
		ParsedWorkbookData workbook,
		Dictionary<string, DivisionBuildState> divisionsByKey,
		Dictionary<string, TeamBuildState> teamsByCode,
		Dictionary<string, string> poolNameToDivisionKey,
		IReadOnlyList<ScheduleCellOverrideDto>? cellOverrides,
		List<BracketCellMappingPreviewDto> bracketMappings,
		List<ScheduleImportValidationErrorDto> errors)
	{
		var overridesByCell = (cellOverrides ?? [])
			.Where(o => !string.IsNullOrWhiteSpace(o.CellReference))
			.GroupBy(o => o.CellReference.Trim(), StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

		var colorMap = BuildDynamicColorMap(workbook, divisionsByKey, teamsByCode);
		var fieldBookings = new Dictionary<(DateTime DateTime, string Location), string>();
		var candidateBracketCells = new List<ParsedScheduleCell>();

		foreach (var cell in workbook.ScheduleCells)
		{
			if (overridesByCell.TryGetValue(cell.CellReference, out var explicitOverride) &&
				(explicitOverride.Ignore || explicitOverride.Slot is not null || !string.IsNullOrWhiteSpace(explicitOverride.DivisionKey)))
			{
				candidateBracketCells.Add(cell);
				continue;
			}

			var poolMatch = PoolPlayMatchupRegex.Match(cell.RawText);
			if (poolMatch.Success)
			{
				var homeCode = poolMatch.Groups["home"].Value.ToUpperInvariant();
				var awayCode = poolMatch.Groups["away"].Value.ToUpperInvariant();

				RegisterFieldBooking(cell, fieldBookings, errors);

				var hasHome = teamsByCode.TryGetValue(homeCode, out var homeTeam);
				var hasAway = teamsByCode.TryGetValue(awayCode, out var awayTeam);

				if (!hasHome)
				{
					errors.Add(new ScheduleImportValidationErrorDto
					{
						Code = "UnknownTeamCode",
						SheetName = "Schedule",
						CellReference = cell.CellReference,
						Message = $"Schedule matchup '{cell.RawText}' references unknown team code '{homeCode}'.",
					});
				}

				if (!hasAway)
				{
					errors.Add(new ScheduleImportValidationErrorDto
					{
						Code = "UnknownTeamCode",
						SheetName = "Schedule",
						CellReference = cell.CellReference,
						Message = $"Schedule matchup '{cell.RawText}' references unknown team code '{awayCode}'.",
					});
				}

				if (!hasHome || !hasAway)
				{
					continue;
				}

				if (string.Equals(homeCode, awayCode, StringComparison.OrdinalIgnoreCase))
				{
					errors.Add(new ScheduleImportValidationErrorDto
					{
						Code = "SelfMatchup",
						SheetName = "Schedule",
						CellReference = cell.CellReference,
						Message = $"Team '{homeCode}' cannot play itself ('{cell.RawText}').",
					});
					continue;
				}

				if (!string.Equals(homeTeam!.PoolName, awayTeam!.PoolName, StringComparison.OrdinalIgnoreCase))
				{
					errors.Add(new ScheduleImportValidationErrorDto
					{
						Code = "CrossPoolMatchup",
						SheetName = "Schedule",
						CellReference = cell.CellReference,
						Message = $"Pool-play matchup '{cell.RawText}' pairs teams from different pools ('{homeTeam.PoolName}' and '{awayTeam.PoolName}').",
					});
					continue;
				}

				var divState = divisionsByKey[homeTeam.DivisionKey];
				divState.PoolPlayGames.Add(new PoolPlayGameBuildState
				{
					CellReference = cell.CellReference,
					PoolName = homeTeam.PoolName,
					ScheduledDateTime = cell.ScheduledDateTime,
					Location = cell.Location,
					HomeTeamCode = homeCode,
					AwayTeamCode = awayCode,
				});

				continue;
			}

			candidateBracketCells.Add(cell);
		}

		// Resolve candidate bracket cells using overrides -> pool letters in text -> dynamic workbook color map -> slot heuristics.
		foreach (var cell in candidateBracketCells)
		{
			overridesByCell.TryGetValue(cell.CellReference, out var cellOverride);

			if (cellOverride?.Ignore == true)
			{
				bracketMappings.Add(new BracketCellMappingPreviewDto
				{
					CellReference = cell.CellReference,
					RawText = cell.RawText,
					ScheduledDateTime = cell.ScheduledDateTime,
					Location = cell.Location,
					FillRgb = cell.FillRgb,
					IsIgnored = true,
					IsAutoResolved = false,
				});
				continue;
			}

			RegisterFieldBooking(cell, fieldBookings, errors);

			var normalizedText = NormalizeScheduleLabel(cell.RawText);
			var isAutoDivision = true;
			DivisionBuildState? resolvedDivision = null;

			if (!string.IsNullOrWhiteSpace(cellOverride?.DivisionKey))
			{
				isAutoDivision = false;
				resolvedDivision = ResolveDivisionByKeyOrName(cellOverride.DivisionKey!, divisionsByKey);
				if (resolvedDivision is null)
				{
					errors.Add(new ScheduleImportValidationErrorDto
					{
						Code = "InvalidOverrideDivision",
						SheetName = "Schedule",
						CellReference = cell.CellReference,
						Message = $"Cell override for '{cell.CellReference}' specifies unknown division '{cellOverride.DivisionKey}'.",
					});
				}
			}
			else
			{
				resolvedDivision = TryResolveDivisionFromCell(
					cell,
					normalizedText,
					divisionsByKey,
					poolNameToDivisionKey,
					colorMap);
			}

			var isAutoSlot = true;
			BracketSlot? resolvedSlot;
			if (cellOverride?.Slot is not null)
			{
				isAutoSlot = false;
				resolvedSlot = cellOverride.Slot.Value;
			}
			else
			{
				var poolNames = resolvedDivision?.Pools.Select(p => p.PoolName).ToList();
				var divSize = resolvedDivision is { HasValidSize: true } ? resolvedDivision.Size : (DivisionSize?)null;
				resolvedSlot = TryResolveBracketSlot(normalizedText, divSize, poolNames);
			}

			bracketMappings.Add(new BracketCellMappingPreviewDto
			{
				CellReference = cell.CellReference,
				RawText = cell.RawText,
				ScheduledDateTime = cell.ScheduledDateTime,
				Location = cell.Location,
				FillRgb = cell.FillRgb,
				ResolvedDivisionKey = resolvedDivision?.DivisionKey,
				ResolvedDivisionName = resolvedDivision?.DivisionName,
				ResolvedSlot = resolvedSlot,
				IsIgnored = false,
				IsAutoResolved = isAutoDivision && isAutoSlot && resolvedDivision is not null && resolvedSlot is not null,
			});

			if (resolvedDivision is null || resolvedSlot is null)
			{
				var missingPart = resolvedDivision is null && resolvedSlot is null
					? "division and bracket slot"
					: resolvedDivision is null
						? "division"
						: "bracket slot";

				errors.Add(new ScheduleImportValidationErrorDto
				{
					Code = "UnresolvedBracketCell",
					SheetName = "Schedule",
					CellReference = cell.CellReference,
					Message = $"Could not automatically determine the {missingPart} for schedule cell '{cell.RawText}' ({cell.CellReference}). Assign an override on the preview screen.",
				});
				continue;
			}

			if (resolvedDivision.HasValidSize && !DivisionSizeRules.HasChampionshipBracket(resolvedDivision.Size))
			{
				errors.Add(new ScheduleImportValidationErrorDto
				{
					Code = "UnexpectedBracketGame",
					SheetName = "Schedule",
					CellReference = cell.CellReference,
					Message = $"Division '{resolvedDivision.DivisionName}' ({(int)resolvedDivision.Size} teams) does not have a championship bracket, but cell '{cell.CellReference}' ('{cell.RawText}') maps to it.",
				});
				continue;
			}

			resolvedDivision.ScheduledBracketCells.Add(new ScheduledBracketCellState
			{
				CellReference = cell.CellReference,
				Slot = resolvedSlot.Value,
				ScheduledDateTime = cell.ScheduledDateTime,
				Location = cell.Location,
			});
		}

		// Assign chronological pool-play rounds, validate round-robin completeness, and match championship shells.
		foreach (var divState in divisionsByKey.Values)
		{
			if (!divState.HasValidSize)
			{
				continue;
			}

			ValidateAndAssignPoolRounds(divState, errors);
			ValidateAndBuildChampionshipGames(divState, errors);
		}
	}

	private static void RegisterFieldBooking(
		ParsedScheduleCell cell,
		Dictionary<(DateTime DateTime, string Location), string> fieldBookings,
		List<ScheduleImportValidationErrorDto> errors)
	{
		var normalizedLocation = cell.Location.Trim().ToUpperInvariant();
		var key = (cell.ScheduledDateTime, normalizedLocation);

		if (fieldBookings.TryGetValue(key, out var existingRef))
		{
			errors.Add(new ScheduleImportValidationErrorDto
			{
				Code = "FieldConflict",
				SheetName = "Schedule",
				CellReference = cell.CellReference,
				Message = $"Overlapping games scheduled on '{cell.Location}' at {cell.ScheduledDateTime:ddd M/d h:mm tt} (cells {existingRef} and {cell.CellReference}).",
			});
		}
		else
		{
			fieldBookings[key] = cell.CellReference;
		}
	}

	private static Dictionary<string, string> BuildDynamicColorMap(
		ParsedWorkbookData workbook,
		IReadOnlyDictionary<string, DivisionBuildState> divisionsByKey,
		IReadOnlyDictionary<string, TeamBuildState> teamsByCode)
	{
		var colorToDivisionKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		// 1. Explicit Color Key legend on the Schedule sheet.
		foreach (var entry in workbook.ColorKeyEntries)
		{
			if (string.IsNullOrWhiteSpace(entry.FillRgb))
			{
				continue;
			}

			try
			{
				var (age, gender) = DivisionKeyHelper.ParseDivisionName(entry.Label);
				var key = DivisionKeyHelper.ToKey(age, gender);
				if (divisionsByKey.ContainsKey(key))
				{
					colorToDivisionKey.TryAdd(entry.FillRgb, key);
				}
			}
			catch (ArgumentException)
			{
				// Non-division label in legend; ignore.
			}
		}

		// 2. Empirical fills on unambiguous pool-play cells in this same Schedule sheet.
		var empiricalByColor = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (var cell in workbook.ScheduleCells)
		{
			if (string.IsNullOrWhiteSpace(cell.FillRgb))
			{
				continue;
			}

			var m = PoolPlayMatchupRegex.Match(cell.RawText);
			if (!m.Success)
			{
				continue;
			}

			var homeCode = m.Groups["home"].Value.ToUpperInvariant();
			if (teamsByCode.TryGetValue(homeCode, out var team))
			{
				if (!empiricalByColor.TryGetValue(cell.FillRgb, out var divSet))
				{
					divSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					empiricalByColor[cell.FillRgb] = divSet;
				}

				divSet.Add(team.DivisionKey);
			}
		}

		foreach (var (rgb, divSet) in empiricalByColor)
		{
			if (divSet.Count == 1)
			{
				colorToDivisionKey.TryAdd(rgb, divSet.First());
			}
		}

		// 3. Division header fills from the Pools sheet.
		foreach (var div in divisionsByKey.Values)
		{
			if (!string.IsNullOrWhiteSpace(div.HeaderFillRgb))
			{
				colorToDivisionKey.TryAdd(div.HeaderFillRgb, div.DivisionKey);
			}
		}

		return colorToDivisionKey;
	}

	private static DivisionBuildState? ResolveDivisionByKeyOrName(
		string keyOrName,
		IReadOnlyDictionary<string, DivisionBuildState> divisionsByKey)
	{
		if (divisionsByKey.TryGetValue(keyOrName.Trim(), out var direct))
		{
			return direct;
		}

		try
		{
			var (age, gender) = DivisionKeyHelper.ParseDivisionName(keyOrName);
			var canonicalKey = DivisionKeyHelper.ToKey(age, gender);
			return divisionsByKey.GetValueOrDefault(canonicalKey);
		}
		catch (ArgumentException)
		{
			return null;
		}
	}

	private static DivisionBuildState? TryResolveDivisionFromCell(
		ParsedScheduleCell cell,
		string normalizedText,
		IReadOnlyDictionary<string, DivisionBuildState> divisionsByKey,
		IReadOnlyDictionary<string, string> poolNameToDivisionKey,
		IReadOnlyDictionary<string, string> colorMap)
	{
		// Signal 1: Explicit uppercase pool letters in cell text (e.g. "3rd A vs B", "Championship 1st E vs 1st F").
		var matchedDivKeysFromPools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (Match m in UppercaseSingleLetterRegex.Matches(normalizedText))
		{
			var letter = m.Groups["letter"].Value;
			if (poolNameToDivisionKey.TryGetValue(letter, out var divKey))
			{
				matchedDivKeysFromPools.Add(divKey);
			}
		}

		if (matchedDivKeysFromPools.Count == 1 &&
			divisionsByKey.TryGetValue(matchedDivKeysFromPools.First(), out var poolResolvedDiv))
		{
			return poolResolvedDiv;
		}

		// Signal 2: Exact or close match in this workbook's dynamic color map.
		if (!string.IsNullOrWhiteSpace(cell.FillRgb))
		{
			if (colorMap.TryGetValue(cell.FillRgb, out var exactDivKey) &&
				divisionsByKey.TryGetValue(exactDivKey, out var exactDiv))
			{
				return exactDiv;
			}

			var fuzzyDivKeys = colorMap
				.Where(kvp => AreColorsWithinTolerance(cell.FillRgb!, kvp.Key, MaxColorChannelTolerance))
				.Select(kvp => kvp.Value)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			if (fuzzyDivKeys.Count == 1 &&
				divisionsByKey.TryGetValue(fuzzyDivKeys[0], out var fuzzyDiv))
			{
				return fuzzyDiv;
			}
		}

		return null;
	}

	private static bool AreColorsWithinTolerance(string hexA, string hexB, int maxChannelDiff)
	{
		if (!TryParseRgb(hexA, out var rA, out var gA, out var bA) ||
			!TryParseRgb(hexB, out var rB, out var gB, out var bB))
		{
			return false;
		}

		return Math.Abs(rA - rB) <= maxChannelDiff &&
			Math.Abs(gA - gB) <= maxChannelDiff &&
			Math.Abs(bA - bB) <= maxChannelDiff;
	}

	private static bool TryParseRgb(string hex, out int r, out int g, out int b)
	{
		r = g = b = 0;
		if (hex.Length != 6)
		{
			return false;
		}

		return int.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r) &&
			int.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g) &&
			int.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
	}

	private static string NormalizeScheduleLabel(string rawText)
	{
		var withNormalizedVs = VersusNormalizeRegex.Replace(rawText.Trim(), " vs ");
		return WhitespaceCollapseRegex.Replace(withNormalizedVs, " ").Trim();
	}

	private static BracketSlot? TryResolveBracketSlot(
		string normalizedText,
		DivisionSize? divisionSize,
		IReadOnlyList<string>? orderedPoolNames)
	{
		var lower = normalizedText.ToLowerInvariant();

		// 1. Explicit Semifinal wording (e.g. "Semifinal 1", "SF1", "SF 2").
		if (lower.Contains("semifinal", StringComparison.Ordinal) ||
			Regex.IsMatch(lower, @"\bsf\s*[12]?\b", RegexOptions.CultureInvariant))
		{
			if (Regex.IsMatch(lower, @"(?:semifinal|sf)\s*1\b", RegexOptions.CultureInvariant))
			{
				return BracketSlot.Semifinal1;
			}

			if (Regex.IsMatch(lower, @"(?:semifinal|sf)\s*2\b", RegexOptions.CultureInvariant))
			{
				return BracketSlot.Semifinal2;
			}
		}

		// 2. 6-team cross-pool Semifinal seedings (e.g. "1st A vs 2nd B", "1st Pool 1 v 2nd Pool 2").
		if ((divisionSize is null or DivisionSize.Six) &&
			!lower.StartsWith("champ", StringComparison.Ordinal) &&
			!lower.StartsWith("consol", StringComparison.Ordinal))
		{
			var forwardMatch = SemifinalSeedForwardRegex.Match(lower);
			if (forwardMatch.Success)
			{
				return ResolveSemifinalSlotByFirstSeedPool(forwardMatch.Groups["firstPool"].Value, orderedPoolNames);
			}

			var reverseMatch = SemifinalSeedReverseRegex.Match(lower);
			if (reverseMatch.Success)
			{
				return ResolveSemifinalSlotByFirstSeedPool(reverseMatch.Groups["firstPool"].Value, orderedPoolNames);
			}
		}

		// 3. 6-team opening Consolation (3rd Pool 1 vs 3rd Pool 2, e.g. "3rd A vs B", "3rds I vs J").
		if ((divisionSize is null or DivisionSize.Six) &&
			(lower.Contains("3rd", StringComparison.Ordinal) || lower.Contains("seed 3", StringComparison.Ordinal)) &&
			!lower.Contains("4th", StringComparison.Ordinal) &&
			!lower.Contains("place", StringComparison.Ordinal) &&
			!lower.Contains("loser", StringComparison.Ordinal))
		{
			return BracketSlot.Consolation;
		}

		// 4. Championship Final (e.g. "Champ", "Championship 1st E vs 1st F", "Championship 1st Pool 1 v 1st Pool 2").
		if (lower.Contains("champ", StringComparison.Ordinal) ||
			Regex.IsMatch(lower, @"\bfinals?\b", RegexOptions.CultureInvariant) ||
			BothSidesFirstSeedRegex.IsMatch(lower) ||
			(divisionSize == DivisionSize.Four && string.Equals(lower, "1st vs 2nd", StringComparison.Ordinal)))
		{
			return BracketSlot.Championship;
		}

		// 5. Consolation / 3rd Place (e.g. "Consol", "Consolation", "3rd Place", "3rd vs 4th", "2nd E vs 2nd F").
		if (lower.Contains("consol", StringComparison.Ordinal) ||
			lower.Contains("3rd place", StringComparison.Ordinal) ||
			lower.Contains("third place", StringComparison.Ordinal) ||
			lower.Contains("3rd vs 4th", StringComparison.Ordinal) ||
			BothSidesSecondSeedRegex.IsMatch(lower) ||
			(lower.Contains("loser", StringComparison.Ordinal) && lower.Contains(" vs ", StringComparison.Ordinal)))
		{
			return divisionSize == DivisionSize.Four
				? BracketSlot.Consolation
				: BracketSlot.ThirdPlace;
		}

		return null;
	}

	private static BracketSlot? ResolveSemifinalSlotByFirstSeedPool(
		string rawFirstPoolToken,
		IReadOnlyList<string>? orderedPoolNames)
	{
		var cleaned = rawFirstPoolToken
			.Replace("pool", string.Empty, StringComparison.OrdinalIgnoreCase)
			.Trim();

		if (string.Equals(cleaned, "1", StringComparison.OrdinalIgnoreCase))
		{
			return BracketSlot.Semifinal1;
		}

		if (string.Equals(cleaned, "2", StringComparison.OrdinalIgnoreCase))
		{
			return BracketSlot.Semifinal2;
		}

		if (orderedPoolNames is { Count: >= 2 })
		{
			if (string.Equals(cleaned, orderedPoolNames[0], StringComparison.OrdinalIgnoreCase))
			{
				return BracketSlot.Semifinal1;
			}

			if (string.Equals(cleaned, orderedPoolNames[1], StringComparison.OrdinalIgnoreCase))
			{
				return BracketSlot.Semifinal2;
			}
		}

		return null;
	}

	private static void ValidateAndAssignPoolRounds(
		DivisionBuildState divState,
		List<ScheduleImportValidationErrorDto> errors)
	{
		var gamesPerRound = DivisionSizeRules.GetGamesPerPoolRound(divState.Size);
		var expectedGamesPerPool = DivisionSizeRules.GetExpectedGamesPerPool(divState.Size);
		var expectedRounds = DivisionSizeRules.GetPoolPlayRoundCount(divState.Size);

		foreach (var pool in divState.Pools)
		{
			var poolGames = divState.PoolPlayGames
				.Where(g => string.Equals(g.PoolName, pool.PoolName, StringComparison.OrdinalIgnoreCase))
				.OrderBy(g => g.ScheduledDateTime)
				.ThenBy(g => g.Location, StringComparer.OrdinalIgnoreCase)
				.ToList();

			// Check duplicate pairings & team simultaneous conflicts.
			var seenPairings = new HashSet<(string A, string B)>();
			var teamSlots = new HashSet<(string TeamCode, DateTime DateTime)>();

			foreach (var game in poolGames)
			{
				var pair = string.Compare(game.HomeTeamCode, game.AwayTeamCode, StringComparison.OrdinalIgnoreCase) <= 0
					? (game.HomeTeamCode.ToUpperInvariant(), game.AwayTeamCode.ToUpperInvariant())
					: (game.AwayTeamCode.ToUpperInvariant(), game.HomeTeamCode.ToUpperInvariant());

				if (!seenPairings.Add(pair))
				{
					errors.Add(new ScheduleImportValidationErrorDto
					{
						Code = "DuplicatePairing",
						SheetName = "Schedule",
						CellReference = game.CellReference,
						Message = $"Duplicate pool-play pairing '{game.HomeTeamCode} vs {game.AwayTeamCode}' in Pool {pool.PoolName} ({divState.DivisionName}).",
					});
				}

				if (!teamSlots.Add((game.HomeTeamCode.ToUpperInvariant(), game.ScheduledDateTime)))
				{
					errors.Add(new ScheduleImportValidationErrorDto
					{
						Code = "TeamTimeConflict",
						SheetName = "Schedule",
						CellReference = game.CellReference,
						Message = $"Team '{game.HomeTeamCode}' is scheduled for multiple games at {game.ScheduledDateTime:ddd M/d h:mm tt}.",
					});
				}

				if (!teamSlots.Add((game.AwayTeamCode.ToUpperInvariant(), game.ScheduledDateTime)))
				{
					errors.Add(new ScheduleImportValidationErrorDto
					{
						Code = "TeamTimeConflict",
						SheetName = "Schedule",
						CellReference = game.CellReference,
						Message = $"Team '{game.AwayTeamCode}' is scheduled for multiple games at {game.ScheduledDateTime:ddd M/d h:mm tt}.",
					});
				}
			}

			// Verify every pair of teams in the pool plays once.
			for (var i = 0; i < pool.Teams.Count; i++)
			{
				for (var j = i + 1; j < pool.Teams.Count; j++)
				{
					var codeA = pool.Teams[i].Code.ToUpperInvariant();
					var codeB = pool.Teams[j].Code.ToUpperInvariant();
					var pair = string.Compare(codeA, codeB, StringComparison.OrdinalIgnoreCase) <= 0
						? (codeA, codeB)
						: (codeB, codeA);

					if (!seenPairings.Contains(pair))
					{
						errors.Add(new ScheduleImportValidationErrorDto
						{
							Code = "MissingPoolPairing",
							SheetName = "Schedule",
							CellReference = string.Empty,
							Message = $"Pool {pool.PoolName} ({divState.DivisionName}) is missing round-robin matchup '{codeA} vs {codeB}'.",
						});
					}
				}
			}

			if (poolGames.Count != expectedGamesPerPool)
			{
				errors.Add(new ScheduleImportValidationErrorDto
				{
					Code = "InvalidPoolGameCount",
					SheetName = "Schedule",
					CellReference = string.Empty,
					Message = $"Pool {pool.PoolName} ({divState.DivisionName}) has {poolGames.Count} scheduled pool-play game(s); expected {expectedGamesPerPool}.",
				});
			}

			// Assign chronological 1-based rounds grouping disjoint-team games up to gamesPerRound.
			var currentRound = 1;
			var countInRound = 0;
			var teamsInRound = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var game in poolGames)
			{
				if (countInRound >= gamesPerRound ||
					teamsInRound.Contains(game.HomeTeamCode) ||
					teamsInRound.Contains(game.AwayTeamCode))
				{
					currentRound++;
					countInRound = 0;
					teamsInRound.Clear();
				}

				game.Round = currentRound;
				countInRound++;
				teamsInRound.Add(game.HomeTeamCode);
				teamsInRound.Add(game.AwayTeamCode);
			}

			if (poolGames.Count == expectedGamesPerPool && currentRound > expectedRounds)
			{
				errors.Add(new ScheduleImportValidationErrorDto
				{
					Code = "InvalidPoolRoundStructure",
					SheetName = "Schedule",
					CellReference = string.Empty,
					Message = $"Pool {pool.PoolName} ({divState.DivisionName}) games span {currentRound} rounds; expected {expectedRounds} rounds.",
				});
			}
		}

		divState.PoolPlayGames.Sort((a, b) =>
		{
			var cmp = a.Round.CompareTo(b.Round);
			if (cmp != 0)
			{
				return cmp;
			}

			cmp = a.ScheduledDateTime.CompareTo(b.ScheduledDateTime);
			return cmp != 0
				? cmp
				: string.Compare(a.PoolName, b.PoolName, StringComparison.OrdinalIgnoreCase);
		});
	}

	private static void ValidateAndBuildChampionshipGames(
		DivisionBuildState divState,
		List<ScheduleImportValidationErrorDto> errors)
	{
		var poolNames = divState.Pools.Select(p => p.PoolName).ToList();
		var expectedDefinitions = ChampionshipBracketBuilder.GetSlotDefinitions(divState.Size, poolNames);

		if (expectedDefinitions.Count == 0)
		{
			return;
		}

		var scheduledBySlot = divState.ScheduledBracketCells
			.GroupBy(c => c.Slot)
			.ToDictionary(g => g.Key, g => g.OrderBy(c => c.ScheduledDateTime).ToList());

		foreach (var (slot, cells) in scheduledBySlot)
		{
			if (expectedDefinitions.All(d => d.Slot != slot))
			{
				foreach (var unexpected in cells)
				{
					errors.Add(new ScheduleImportValidationErrorDto
					{
						Code = "UnexpectedBracketSlot",
						SheetName = "Schedule",
						CellReference = unexpected.CellReference,
						Message = $"Division '{divState.DivisionName}' ({(int)divState.Size} teams) does not use bracket slot '{slot}'.",
					});
				}

				continue;
			}

			if (cells.Count > 1)
			{
				foreach (var dup in cells.Skip(1))
				{
					errors.Add(new ScheduleImportValidationErrorDto
					{
						Code = "DuplicateBracketSlot",
						SheetName = "Schedule",
						CellReference = dup.CellReference,
						Message = $"Division '{divState.DivisionName}' has multiple cells mapped to bracket slot '{slot}' ({cells[0].CellReference} and {dup.CellReference}).",
					});
				}
			}
		}

		foreach (var def in expectedDefinitions)
		{
			if (!scheduledBySlot.TryGetValue(def.Slot, out var matchingCells) || matchingCells.Count == 0)
			{
				errors.Add(new ScheduleImportValidationErrorDto
				{
					Code = "MissingBracketSlot",
					SheetName = "Schedule",
					CellReference = string.Empty,
					Message = $"Division '{divState.DivisionName}' is missing scheduled championship game for '{def.Slot}' ({def.HomeSourceDescription} vs {def.AwaySourceDescription}).",
				});

				divState.ChampionshipGames.Add(new ChampionshipGameBuildState
				{
					CellReference = string.Empty,
					Slot = def.Slot,
					HomeSourceDescription = def.HomeSourceDescription,
					AwaySourceDescription = def.AwaySourceDescription,
				});
			}
			else
			{
				var scheduled = matchingCells[0];
				divState.ChampionshipGames.Add(new ChampionshipGameBuildState
				{
					CellReference = scheduled.CellReference,
					Slot = def.Slot,
					ScheduledDateTime = scheduled.ScheduledDateTime,
					Location = scheduled.Location,
					HomeSourceDescription = def.HomeSourceDescription,
					AwaySourceDescription = def.AwaySourceDescription,
				});
			}
		}
	}

	private static ImportedDivisionPreviewDto BuildDivisionPreviewDto(DivisionBuildState divState)
	{
		var shootoutRoundCount = divState.HasValidSize
			? DivisionSizeRules.GetShootoutRoundCount(divState.Size)
			: 0;

		var maxPoolPsoRound = divState.Size == DivisionSize.Six ? 3 : shootoutRoundCount;
		var poolPsoMatchups = divState.HasValidSize
			? divState.PoolPlayGames.Count(g => g.Round > 0 && g.Round <= maxPoolPsoRound)
			: 0;
		var bracketPsoMatchups = divState.HasValidSize && divState.Size == DivisionSize.Six ? 3 : 0;

		return new ImportedDivisionPreviewDto
		{
			DivisionKey = divState.DivisionKey,
			DivisionName = divState.DivisionName,
			AgeGroup = divState.AgeGroup,
			Gender = divState.Gender,
			Size = divState.Size,
			ShootoutRoundCount = shootoutRoundCount,
			ShootoutMatchupCount = poolPsoMatchups + bracketPsoMatchups,
			Pools = divState.Pools.Select(p => new ImportedPoolPreviewDto
			{
				PoolName = p.PoolName,
				Teams = p.Teams.Select(t => new ImportedTeamPreviewDto
				{
					Code = t.Code,
					PoolName = t.PoolName,
					TeamNumber = t.TeamNumber,
					RegionNumber = t.RegionNumber,
					RegionName = t.RegionName,
					CoachName = t.CoachName,
					DisplayName = $"{t.RegionName}: {t.CoachName}",
				}).ToList(),
			}).ToList(),
			PoolPlayGames = divState.PoolPlayGames.Select(g => new ImportedPoolPlayGamePreviewDto
			{
				CellReference = g.CellReference,
				PoolName = g.PoolName,
				Round = g.Round,
				ScheduledDateTime = g.ScheduledDateTime,
				Location = g.Location,
				HomeTeamCode = g.HomeTeamCode,
				AwayTeamCode = g.AwayTeamCode,
			}).ToList(),
			ChampionshipGames = divState.ChampionshipGames.Select(c => new ImportedChampionshipGamePreviewDto
			{
				CellReference = c.CellReference,
				Slot = c.Slot,
				ScheduledDateTime = c.ScheduledDateTime,
				Location = c.Location,
				HomeSourceDescription = c.HomeSourceDescription,
				AwaySourceDescription = c.AwaySourceDescription,
			}).ToList(),
		};
	}

	private sealed class DivisionBuildState
	{
		public required string DivisionKey { get; init; }

		public required string DivisionName { get; init; }

		public AgeGroup AgeGroup { get; init; }

		public Gender Gender { get; init; }

		public required string HeaderCellReference { get; init; }

		public string? HeaderFillRgb { get; init; }

		public DivisionSize Size { get; set; }

		public bool HasValidSize { get; set; }

		public List<PoolBuildState> Pools { get; } = new();

		public List<PoolPlayGameBuildState> PoolPlayGames { get; } = new();

		public List<ScheduledBracketCellState> ScheduledBracketCells { get; } = new();

		public List<ChampionshipGameBuildState> ChampionshipGames { get; } = new();
	}

	private sealed class PoolBuildState
	{
		public required string PoolName { get; init; }

		public required List<TeamBuildState> Teams { get; init; }
	}

	private sealed class TeamBuildState
	{
		public required string Code { get; init; }

		public required string PoolName { get; init; }

		public int TeamNumber { get; init; }

		public int RegionNumber { get; init; }

		public required string RegionName { get; init; }

		public required string CoachName { get; init; }

		public required string DivisionKey { get; init; }

		public required string DivisionName { get; init; }
	}

	private sealed class PoolPlayGameBuildState
	{
		public required string CellReference { get; init; }

		public required string PoolName { get; init; }

		public int Round { get; set; }

		public DateTime ScheduledDateTime { get; init; }

		public required string Location { get; init; }

		public required string HomeTeamCode { get; init; }

		public required string AwayTeamCode { get; init; }
	}

	private sealed class ScheduledBracketCellState
	{
		public required string CellReference { get; init; }

		public BracketSlot Slot { get; init; }

		public DateTime ScheduledDateTime { get; init; }

		public required string Location { get; init; }
	}

	private sealed class ChampionshipGameBuildState
	{
		public required string CellReference { get; init; }

		public BracketSlot Slot { get; init; }

		public DateTime? ScheduledDateTime { get; init; }

		public string Location { get; init; } = string.Empty;

		public required string HomeSourceDescription { get; init; }

		public required string AwaySourceDescription { get; init; }
	}
}
