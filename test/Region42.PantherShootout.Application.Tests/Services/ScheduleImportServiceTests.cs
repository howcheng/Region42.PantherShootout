using Moq;
using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Exceptions;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Application.Services;
using Region42.PantherShootout.Domain;
using Region42.PantherShootout.Domain.Common;
using Region42.PantherShootout.Domain.Documents;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;
using Region42.PantherShootout.Domain.Helpers;
using Region42.PantherShootout.Domain.Interfaces;
using Region42.PantherShootout.Domain.Models;
using Xunit;

namespace Region42.PantherShootout.Application.Tests.Services;

public class ScheduleImportServiceTests
{
	[Fact]
	public async Task ValidateAndImport_2025StyleWorkbook_PopulatesDiscreteTeamsPoolGamesBracketSchedulesAndPsoRounds()
	{
		var ctx = new InMemoryTestTournamentDataContext();
		var year = new TournamentYear { Id = 1, Year = 2025, Name = "2025 Panther Shootout", IsActive = true };
		ctx.Add(year);

		var workbook = BuildTwoDivision2025StyleWorkbook();
		var service = CreateService(ctx, workbook);

		await using var previewStream = new MemoryStream([1, 2, 3]);
		var preview = await service.ValidateWorkbookAsync(previewStream, year.Id);

		Assert.True(preview.IsValid);
		Assert.Empty(preview.Errors);
		Assert.Equal(2, preview.Divisions.Count);

		// 10U Girls: 6 teams (Pools A & B), 6 pool games, 5 bracket games, 4 PSO rounds (9 PSO matchups: 6 pool + 3 R4)
		var u10g = preview.Divisions.Single(d => d.DivisionKey == "10u-girls");
		Assert.Equal(DivisionSize.Six, u10g.Size);
		Assert.Equal(4, u10g.ShootoutRoundCount);
		Assert.Equal(9, u10g.ShootoutMatchupCount);
		Assert.Equal(6, u10g.PoolPlayGames.Count);
		Assert.Equal(5, u10g.ChampionshipGames.Count);

		var teamA1 = u10g.Pools[0].Teams[0];
		Assert.Equal("A1", teamA1.Code);
		Assert.Equal(121, teamA1.RegionNumber);
		Assert.Equal("Simi Valley", teamA1.RegionName);
		Assert.Equal("Wright", teamA1.CoachName);
		Assert.Equal("Simi Valley: Wright", teamA1.DisplayName);

		// 12U Boys: 4 teams (Pool G), 6 pool games, 2 bracket games (Consolation + Championship), 3 PSO rounds (6 matchups)
		var u12b = preview.Divisions.Single(d => d.DivisionKey == "12u-boys");
		Assert.Equal(DivisionSize.Four, u12b.Size);
		Assert.Equal(3, u12b.ShootoutRoundCount);
		Assert.Equal(6, u12b.ShootoutMatchupCount);
		Assert.Equal(2, u12b.ChampionshipGames.Count);
		Assert.Contains(u12b.ChampionshipGames, g => g.Slot == BracketSlot.Championship && g.Location == "Field 1" && g.ScheduledDateTime is not null);
		Assert.Contains(u12b.ChampionshipGames, g => g.Slot == BracketSlot.Consolation && g.Location == "Field 2" && g.ScheduledDateTime is not null);

		// Step 2: Confirm Import
		await using var commitStream = new MemoryStream([1, 2, 3]);
		var commitResult = await service.ImportWorkbookAsync(commitStream, year.Id);

		Assert.Equal(2, commitResult.DivisionsImported);
		Assert.Equal(3, commitResult.PoolsImported);
		Assert.Equal(10, commitResult.TeamsImported);
		Assert.Equal(12, commitResult.PoolPlayGamesImported);
		Assert.Equal(7, commitResult.ChampionshipGamesImported);
		Assert.Equal(15, commitResult.ShootoutRoundsImported); // 9 for 10UG + 6 for 12UB

		var persistedTeamA1 = ctx.Teams.Single(t => t.CoachName == "Wright");
		Assert.Equal("A1", persistedTeamA1.Code);
		Assert.Equal(121, persistedTeamA1.RegionNumber);
		Assert.Equal("Simi Valley", persistedTeamA1.RegionName);
		Assert.Equal("Simi Valley: Wright", persistedTeamA1.Name);

		// Verify 6-team Round 4 PSO shells are linked to opening ChampionshipGames
		var u10gEntity = ctx.Divisions.Single(d => d.AgeGroup == AgeGroup.U10 && d.Gender == Gender.Girls);
		var round4Rows = ctx.ShootoutRounds.Where(r => r.DivisionId == u10gEntity.Id && r.Round == 4).ToList();
		Assert.Equal(3, round4Rows.Count);
		Assert.All(round4Rows, r => Assert.NotNull(r.ChampionshipGameId));
		Assert.NotNull(ctx.GetStandingsForDivision(u10gEntity.Id));
	}

	[Fact]
	public async Task ValidateWorkbookAsync_Handles2024And2022LabelVariations_AndSupportsCellOverrides()
	{
		var ctx = new InMemoryTestTournamentDataContext();
		var year = new TournamentYear { Id = 1, Year = 2026, Name = "2026 Panther Shootout", IsActive = true };
		ctx.Add(year);

		// 12U Girls (Pools E & F, 8 teams) using 2024 wording ("Championship 1st E vs 1st F", NO fill color!)
		// + 10U Boys (Pools C & D, 8 teams) using 2022 wording ("Championship 1st Pool 1 v 1st Pool 2") AND
		// a custom unrecognized label ("Bronze Showdown") + a non-game note cell ("Lunch Break").
		var workbook = BuildHistoricalVariationsWorkbook();
		var service = CreateService(ctx, workbook);

		await using var stream1 = new MemoryStream([1]);
		var firstPass = await service.ValidateWorkbookAsync(stream1, year.Id);

		// 2024 uncolored "Championship 1st E vs 1st F" and "Consolation 2nd E vs 2nd F" auto-resolve via pool letters E & F!
		var eChampMapping = firstPass.BracketCellMappings.Single(m => m.CellReference == "B20");
		Assert.Equal("12u-girls", eChampMapping.ResolvedDivisionKey);
		Assert.Equal(BracketSlot.Championship, eChampMapping.ResolvedSlot);
		Assert.True(eChampMapping.IsAutoResolved);

		// 2022 "Championship 1st Pool 1 v 1st Pool 2" auto-resolves via empirical pool-play cell fill + seed wording!
		var cChampMapping = firstPass.BracketCellMappings.Single(m => m.CellReference == "D20");
		Assert.Equal("10u-boys", cChampMapping.ResolvedDivisionKey);
		Assert.Equal(BracketSlot.Championship, cChampMapping.ResolvedSlot);
		Assert.True(cChampMapping.IsAutoResolved);

		// Custom "Bronze Showdown" (E20) and "Lunch Break" (F20) are flagged as unresolved on first pass.
		Assert.False(firstPass.IsValid);
		Assert.Contains(firstPass.Errors, e => e.Code == "UnresolvedBracketCell" && e.CellReference == "E20");
		Assert.Contains(firstPass.Errors, e => e.Code == "UnresolvedBracketCell" && e.CellReference == "F20");

		// Second pass with admin preview overrides resolves both cleanly!
		var overrides = new List<ScheduleCellOverrideDto>
		{
			new() { CellReference = "E20", DivisionKey = "10u-boys", Slot = BracketSlot.ThirdPlace },
			new() { CellReference = "F20", Ignore = true },
		};

		await using var stream2 = new MemoryStream([1]);
		var secondPass = await service.ValidateWorkbookAsync(stream2, year.Id, overrides);

		Assert.True(secondPass.IsValid);
		Assert.Empty(secondPass.Errors);
		var u10b = secondPass.Divisions.Single(d => d.DivisionKey == "10u-boys");
		Assert.Contains(u10b.ChampionshipGames, g => g.Slot == BracketSlot.ThirdPlace && g.CellReference == "E20");
	}

	[Fact]
	public async Task ValidateWorkbookAsync_FiveTeamDivision_CreatesFivePoolRoundsAndCapsPsoAtFourRounds()
	{
		var ctx = new InMemoryTestTournamentDataContext();
		var year = new TournamentYear { Id = 1, Year = 2026, Name = "2026 Panther Shootout", IsActive = true };
		ctx.Add(year);

		var workbook = BuildFiveTeamDivisionWorkbook();
		var service = CreateService(ctx, workbook);

		await using var stream = new MemoryStream([1]);
		var preview = await service.ValidateWorkbookAsync(stream, year.Id);

		Assert.True(preview.IsValid);
		var div = Assert.Single(preview.Divisions);
		Assert.Equal(DivisionSize.Five, div.Size);
		Assert.Equal(10, div.PoolPlayGames.Count);
		Assert.Equal(5, div.PoolPlayGames.Max(g => g.Round));
		Assert.Empty(div.ChampionshipGames);
		Assert.Equal(4, div.ShootoutRoundCount);
		Assert.Equal(8, div.ShootoutMatchupCount); // Rounds 1..4 (2 games/round = 8); Round 5 excluded!
	}

	[Fact]
	public async Task ValidateWorkbookAsync_CollectsAllStructuralAndBusinessRuleErrorsAtOnce()
	{
		var ctx = new InMemoryTestTournamentDataContext();
		var year = new TournamentYear { Id = 1, Year = 2026, Name = "2026 Panther Shootout", IsActive = true };
		ctx.Add(year);

		var invalidWorkbook = new ParsedWorkbookData
		{
			HasPoolsSheet = true,
			HasScheduleSheet = true,
			DivisionBlocks =
			[
				// Invalid 3-team division + bad token + unknown region 9999
				new ParsedDivisionPoolBlock
				{
					HeaderCellReference = "B1",
					DivisionHeaderText = "10U Girls",
					Pools =
					[
						new ParsedPoolColumn
						{
							PoolColumnOrdinal = 1,
							HeaderText = "Pool 1",
							Teams =
							[
								new ParsedPoolTeamRow { CodeCellReference = "A3", RawCode = "A1", TeamCellReference = "B3", RawTeamToken = "10UG-9999-UnknownReg" },
								new ParsedPoolTeamRow { CodeCellReference = "A4", RawCode = "BAD", TeamCellReference = "B4", RawTeamToken = "10UG-42-Carter" },
								new ParsedPoolTeamRow { CodeCellReference = "A5", RawCode = "A2", TeamCellReference = "B5", RawTeamToken = "NotAToken" },
							],
						},
					],
				},
			],
			ScheduleCells =
			[
				new ParsedScheduleCell
				{
					CellReference = "B4",
					ScheduledDateTime = new DateTime(2026, 11, 27, 8, 0, 0),
					Location = "Field 1",
					RawText = "A1 vs Z9",
				},
				new ParsedScheduleCell
				{
					CellReference = "B4_DUP",
					ScheduledDateTime = new DateTime(2026, 11, 27, 8, 0, 0),
					Location = "Field 1",
					RawText = "A1 vs A1",
				},
			],
		};

		var service = CreateService(ctx, invalidWorkbook);
		await using var stream = new MemoryStream([1]);
		var preview = await service.ValidateWorkbookAsync(stream, year.Id);

		Assert.False(preview.IsValid);
		Assert.Contains(9999, preview.MissingRegionNumbers);
		Assert.Contains(preview.Errors, e => e.Code == "MissingRegion" && e.MissingRegionNumber == 9999);
		Assert.Contains(preview.Errors, e => e.Code == "InvalidTeamCode" && e.CellReference == "A4");
		Assert.Contains(preview.Errors, e => e.Code == "InvalidTeamToken" && e.CellReference == "B5");
		Assert.Contains(preview.Errors, e => e.Code == "InvalidDivisionSize");
		Assert.Contains(preview.Errors, e => e.Code == "UnknownTeamCode" && e.CellReference == "B4");
		Assert.Contains(preview.Errors, e => e.Code == "FieldConflict");
		Assert.Contains(preview.Errors, e => e.Code == "SelfMatchup");
	}

	[Fact]
	public async Task ImportWorkbookAsync_EnforcesOverwriteScoresGuard_WriteLock_AndTransactionRollback()
	{
		var ctx = new InMemoryTestTournamentDataContext();
		var year = new TournamentYear { Id = 1, Year = 2025, Name = "2025 Panther Shootout", IsActive = true };
		ctx.Add(year);

		var workbook = BuildTwoDivision2025StyleWorkbook();

		// 1. Write lock contention rejects immediately.
		var contendedLock = new Mock<IStorageWriteLock>();
		contendedLock
			.Setup(l => l.TryAcquireAsync(StorageWriteLockNames.ScheduleImport, It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync((IStorageWriteLockHandle?)null);

		var lockedService = CreateService(ctx, workbook, writeLock: contendedLock.Object);
		await using var lockedStream = new MemoryStream([1]);
		await Assert.ThrowsAsync<InvalidOperationException>(() => lockedService.ImportWorkbookAsync(lockedStream, year.Id));

		// 2. Initial import succeeds, then record a score -> re-import without allowOverwriteScores fails.
		var service = CreateService(ctx, workbook);
		await using (var s1 = new MemoryStream([1]))
		{
			await service.ImportWorkbookAsync(s1, year.Id);
		}

		var firstGame = ctx.PoolPlayGames[0];
		firstGame.HomeGoals = 2;
		firstGame.AwayGoals = 1;

		await using (var s2 = new MemoryStream([1]))
		{
			await Assert.ThrowsAsync<InvalidOperationException>(() =>
				service.ImportWorkbookAsync(s2, year.Id, allowOverwriteScores: false));
		}

		Assert.Equal(2, ctx.PoolPlayGames[0].HomeGoals); // Score untouched!

		// 3. Simulate SaveChangesAsync failure -> transaction rollback preserves pre-import state!
		ctx.ThrowOnSaveChanges = true;
		await using (var s3 = new MemoryStream([1]))
		{
			await Assert.ThrowsAsync<IOException>(() =>
				service.ImportWorkbookAsync(s3, year.Id, allowOverwriteScores: true));
		}

		Assert.Equal(2, ctx.PoolPlayGames[0].HomeGoals); // Rolled back cleanly!

		// 4. With allowOverwriteScores: true and healthy storage, schedule is cleanly replaced and scores reset.
		ctx.ThrowOnSaveChanges = false;
		await using (var s4 = new MemoryStream([1]))
		{
			var result = await service.ImportWorkbookAsync(s4, year.Id, allowOverwriteScores: true);
			Assert.Equal(2, result.DivisionsImported);
		}

		Assert.All(ctx.PoolPlayGames, g => Assert.Null(g.HomeGoals));
	}

	private static ScheduleImportService CreateService(
		InMemoryTestTournamentDataContext context,
		ParsedWorkbookData workbook,
		IStorageWriteLock? writeLock = null)
	{
		var reader = new Mock<IScheduleWorkbookReader>();
		reader.Setup(r => r.ReadWorkbook(It.IsAny<Stream>())).Returns(workbook);

		if (writeLock is null)
		{
			var lockMock = new Mock<IStorageWriteLock>();
			lockMock
				.Setup(l => l.TryAcquireAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(Mock.Of<IStorageWriteLockHandle>());
			writeLock = lockMock.Object;
		}

		var refreshMock = new Mock<IStandingsRefreshService>();
		refreshMock
			.Setup(r => r.RefreshDivisionAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync((int divId, bool _, CancellationToken _) =>
			{
				var snap = new DivisionStandingsSnapshotDocument { CalculatedAt = DateTime.UtcNow };
				context.SetStandingsForDivision(divId, snap);
				return snap;
			});

		var currentUser = Mock.Of<ICurrentUserService>(u => u.UserIdentifier == "admin@aysoregion42.org");

		return new ScheduleImportService(
			reader.Object,
			context,
			writeLock,
			refreshMock.Object,
			currentUser);
	}

	private static ParsedWorkbookData BuildTwoDivision2025StyleWorkbook()
	{
		var fri = new DateTime(2025, 11, 28);
		var sat = new DateTime(2025, 11, 29);

		return new ParsedWorkbookData
		{
			HasPoolsSheet = true,
			HasScheduleSheet = true,
			DivisionBlocks =
			[
				new ParsedDivisionPoolBlock
				{
					HeaderCellReference = "B1",
					DivisionHeaderText = "10U Girls",
					HeaderFillRgb = "FCAEE0",
					Pools =
					[
						new ParsedPoolColumn
						{
							PoolColumnOrdinal = 1,
							HeaderText = "Pool 1",
							Teams =
							[
								Row("A3", "A1", "B3", "10UG-121-Wright"),
								Row("A4", "A2", "B4", "10UG-2-Dykstra"),
								Row("A5", "A3", "B5", "10UG-9-Fersht"),
							],
						},
						new ParsedPoolColumn
						{
							PoolColumnOrdinal = 2,
							HeaderText = "Pool 2",
							Teams =
							[
								Row("C3", "B1", "D3", "10UG-42-Carter"),
								Row("C4", "B2", "D4", "10UG-20-Assefa"),
								Row("C5", "B3", "D5", "10UG-121-Medina"),
							],
						},
					],
				},
				new ParsedDivisionPoolBlock
				{
					HeaderCellReference = "G9",
					DivisionHeaderText = "12U Boys",
					HeaderFillRgb = "BDD6EE",
					Pools =
					[
						new ParsedPoolColumn
						{
							PoolColumnOrdinal = 1,
							HeaderText = "Pool 1",
							Teams =
							[
								Row("F11", "G1", "G11", "12UB-42-Hitt"),
								Row("F12", "G2", "G12", "12UB-20-Ekstein"),
								Row("F13", "G3", "G13", "12UB-2-Cacho"),
								Row("F14", "G4", "G14", "12UB-4-Edwards"),
							],
						},
					],
				},
			],
			ColorKeyEntries =
			[
				new ParsedColorKeyEntry { CellReference = "J5", Label = "10UG", FillRgb = "FCAEE0" },
				new ParsedColorKeyEntry { CellReference = "J8", Label = "12UB", FillRgb = "BDD6EE" },
			],
			ScheduleCells =
			[
				// 10UG Pool Play
				Cell("D5", fri.AddHours(9), "Field 3", "A1 vs A2", "FCAEE0"),
				Cell("E5", fri.AddHours(9), "Field 4", "B1 vs B2", "FCAEE0"),
				Cell("D7", fri.AddHours(11), "Field 3", "A1 vs A3", "FCAEE0"),
				Cell("E7", fri.AddHours(11), "Field 4", "B1 vs B3", "FCAEE0"),
				Cell("D9", fri.AddHours(13), "Field 3", "A2 vs A3", "FCAEE0"),
				Cell("E9", fri.AddHours(13), "Field 4", "B2 vs B3", "FCAEE0"),
				// 12UB Pool Play
				Cell("B6", fri.AddHours(10), "Field 1", "G1 vs G2", "BDD6EE"),
				Cell("C6", fri.AddHours(10), "Field 2", "G3 vs G4", "BDD6EE"),
				Cell("B8", fri.AddHours(12), "Field 1", "G1 vs G3", "BDD6EE"),
				Cell("C8", fri.AddHours(12), "Field 2", "G2 vs G4", "BDD6EE"),
				Cell("B21", sat.AddHours(10), "Field 1", "G1 vs G4", "BDD6EE"),
				Cell("C21", sat.AddHours(10), "Field 2", "G2 vs G3", "BDD6EE"),
				// 10UG Bracket (Consolation, SF1, SF2, Champ, Consol->ThirdPlace)
				Cell("D19", sat.AddHours(8), "Field 3", "3rd A vs B", "FCAEE0"),
				Cell("D21", sat.AddHours(10), "Field 3", "1st A vs 2nd B", "FCAEE0"),
				Cell("E21", sat.AddHours(10), "Field 4", "1st B vs 2nd A", "FCAEE0"),
				Cell("D23", sat.AddHours(13), "Field 3", "Champ", "FCAEE0"),
				Cell("E23", sat.AddHours(13), "Field 4", "Consol", "FCAEE0"),
				// 12UB Bracket (Champ, Consol->Consolation)
				Cell("B24", sat.AddHours(14.5), "Field 1", "Champ", "BDD6EE"),
				Cell("C24", sat.AddHours(14.5), "Field 2", "Consol", "BDD6EE"),
			],
		};
	}

	private static ParsedWorkbookData BuildHistoricalVariationsWorkbook()
	{
		var fri = new DateTime(2026, 11, 27);
		var sat = new DateTime(2026, 11, 28);

		return new ParsedWorkbookData
		{
			HasPoolsSheet = true,
			HasScheduleSheet = true,
			DivisionBlocks =
			[
				new ParsedDivisionPoolBlock
				{
					HeaderCellReference = "B1",
					DivisionHeaderText = "12U Girls",
					Pools =
					[
						PoolCol(1, ("E1", "12UG-42-Chay"), ("E2", "12UG-638-Carper"), ("E3", "12UG-2-Weideman"), ("E4", "12UG-9-Rivera")),
						PoolCol(2, ("F1", "12UG-42-Alamillo"), ("F2", "12UG-795-Podesta"), ("F3", "12UG-20-Assefa"), ("F4", "12UG-9-Biberacher")),
					],
				},
				new ParsedDivisionPoolBlock
				{
					HeaderCellReference = "G1",
					DivisionHeaderText = "10U Boys",
					Pools =
					[
						PoolCol(1, ("C1", "10UB-9-Mallord"), ("C2", "10UB-42-Oakman"), ("C3", "10UB-2-Wymar"), ("C4", "10UB-4-Hartman")),
						PoolCol(2, ("D1", "10UB-9-Furniss"), ("D2", "10UB-42-Cassaro"), ("D3", "10UB-20-Cruz"), ("D4", "10UB-33-Corri")),
					],
				},
			],
			ScheduleCells =
			[
				// 12UG Pool E & F (uncolored)
				Cell("B4", fri.AddHours(8), "Field 1", "E1 vs E2", null),
				Cell("C4", fri.AddHours(8), "Field 2", "E3 vs E4", null),
				Cell("B5", fri.AddHours(9), "Field 1", "F1 vs F2", null),
				Cell("C5", fri.AddHours(9), "Field 2", "F3 vs F4", null),
				Cell("B6", fri.AddHours(11), "Field 1", "E1 vs E3", null),
				Cell("C6", fri.AddHours(11), "Field 2", "E2 vs E4", null),
				Cell("B7", fri.AddHours(12), "Field 1", "F1 vs F3", null),
				Cell("C7", fri.AddHours(12), "Field 2", "F2 vs F4", null),
				Cell("B14", sat.AddHours(8), "Field 1", "E1 vs E4", null),
				Cell("C14", sat.AddHours(8), "Field 2", "E2 vs E3", null),
				Cell("B15", sat.AddHours(9), "Field 1", "F1 vs F4", null),
				Cell("C15", sat.AddHours(9), "Field 2", "F2 vs F3", null),
				// 10UB Pool C & D (using "v" separator and gray fill BFBFBF, NO Color Key legend!)
				Cell("D4", fri.AddHours(8), "Field 3", "C1 v C2", "BFBFBF"),
				Cell("E4", fri.AddHours(8), "Field 4", "C3 v C4", "BFBFBF"),
				Cell("D5", fri.AddHours(9), "Field 3", "D1 v D2", "BFBFBF"),
				Cell("E5", fri.AddHours(9), "Field 4", "D3 v D4", "BFBFBF"),
				Cell("D6", fri.AddHours(11), "Field 3", "C1 v C3", "BFBFBF"),
				Cell("E6", fri.AddHours(11), "Field 4", "C2 v C4", "BFBFBF"),
				Cell("D7", fri.AddHours(12), "Field 3", "D1 v D3", "BFBFBF"),
				Cell("E7", fri.AddHours(12), "Field 4", "D2 v D4", "BFBFBF"),
				Cell("D14", sat.AddHours(8), "Field 3", "C1 v C4", "BFBFBF"),
				Cell("E14", sat.AddHours(8), "Field 4", "C2 v C3", "BFBFBF"),
				Cell("D15", sat.AddHours(9), "Field 3", "D1 v D4", "BFBFBF"),
				Cell("E15", sat.AddHours(9), "Field 4", "D2 v D3", "BFBFBF"),
				// 2024 uncolored Championship/Consolation labels with explicit pool letters
				Cell("B20", sat.AddHours(13), "Field 1", "Championship 1st E vs 1st F", null),
				Cell("C20", sat.AddHours(13), "Field 2", "Consolation 2nd E vs 2nd F", null),
				// 2022 "Pool 1 v Pool 2" label with empirical division fill + custom unresolved label + note cell
				Cell("D20", sat.AddHours(13), "Field 3", "Championship 1st Pool 1 v 1st Pool 2", "BFBFBF"),
				Cell("E20", sat.AddHours(13), "Field 4", "Bronze Showdown", null),
				Cell("F20", sat.AddHours(12), "Field 5", "Lunch Break", null),
			],
		};
	}

	private static ParsedWorkbookData BuildFiveTeamDivisionWorkbook()
	{
		var fri = new DateTime(2026, 11, 27);
		var sat = new DateTime(2026, 11, 28);

		return new ParsedWorkbookData
		{
			HasPoolsSheet = true,
			HasScheduleSheet = true,
			DivisionBlocks =
			[
				new ParsedDivisionPoolBlock
				{
					HeaderCellReference = "B1",
					DivisionHeaderText = "14U Boys",
					Pools =
					[
						PoolCol(
							1,
							("K1", "14UB-2-Perez"),
							("K2", "14UB-4-Finerty"),
							("K3", "14UB-20-Arcari"),
							("K4", "14UB-42-Hiraoka"),
							("K5", "14UB-88-Salcedo")),
					],
				},
			],
			ScheduleCells =
			[
				// Round 1 (K5 bye)
				Cell("B4", fri.AddHours(8), "Field 6", "K1 vs K2", null),
				Cell("C4", fri.AddHours(8), "Field 7", "K3 vs K4", null),
				// Round 2 (K4 bye)
				Cell("B5", fri.AddHours(10), "Field 6", "K1 vs K3", null),
				Cell("C5", fri.AddHours(10), "Field 7", "K2 vs K5", null),
				// Round 3 (K2 bye)
				Cell("B6", fri.AddHours(12), "Field 6", "K1 vs K4", null),
				Cell("C6", fri.AddHours(12), "Field 7", "K3 vs K5", null),
				// Round 4 (K3 bye)
				Cell("B10", sat.AddHours(8), "Field 6", "K1 vs K5", null),
				Cell("C10", sat.AddHours(8), "Field 7", "K2 vs K4", null),
				// Round 5 (K1 bye)
				Cell("B11", sat.AddHours(10), "Field 6", "K2 vs K3", null),
				Cell("C11", sat.AddHours(10), "Field 7", "K4 vs K5", null),
			],
		};
	}

	private static ParsedPoolTeamRow Row(string codeRef, string code, string teamRef, string token) => new()
	{
		CodeCellReference = codeRef,
		RawCode = code,
		TeamCellReference = teamRef,
		RawTeamToken = token,
	};

	private static ParsedPoolColumn PoolCol(int ordinal, params (string Code, string Token)[] teams) => new()
	{
		PoolColumnOrdinal = ordinal,
		HeaderText = $"Pool {ordinal}",
		Teams = teams.Select((t, i) => new ParsedPoolTeamRow
		{
			CodeCellReference = $"A{i + 3}",
			RawCode = t.Code,
			TeamCellReference = $"B{i + 3}",
			RawTeamToken = t.Token,
		}).ToList(),
	};

	private static ParsedScheduleCell Cell(string cellRef, DateTime dt, string field, string text, string? rgb) => new()
	{
		CellReference = cellRef,
		ScheduledDateTime = dt,
		Location = field,
		RawText = text,
		FillRgb = rgb,
	};

	/// <summary>
	/// Lightweight in-memory <see cref="ITournamentDataContext"/> test double with snapshot rollback support.
	/// </summary>
	private sealed class InMemoryTestTournamentDataContext : ITournamentDataContext
	{
		private int _nextId = 100;

		public InMemoryTestTournamentDataContext()
		{
			foreach (var (num, name) in DefaultAysoRegions.Entries)
			{
				RegionsList.Add(new AysoRegion { Id = _nextId++, RegionNumber = num, Name = name });
			}
		}

		public bool ThrowOnSaveChanges { get; set; }

		public TournamentIndexDocument Index { get; } = new();

		public List<AysoRegion> RegionsList { get; } = new();

		public List<TournamentYear> YearsList { get; } = new();

		public List<Division> DivisionsList { get; } = new();

		public List<Pool> PoolsList { get; } = new();

		public List<Team> TeamsList { get; } = new();

		public List<PoolPlayGame> PoolPlayGamesList { get; } = new();

		public List<ChampionshipGame> ChampionshipGamesList { get; } = new();

		public List<ShootoutRound> ShootoutRoundsList { get; } = new();

		public List<TiebreakerOverride> OverridesList { get; } = new();

		public Dictionary<int, DivisionStandingsSnapshotDocument> StandingsMap { get; } = new();

		public IReadOnlyList<AysoRegion> Regions => RegionsList;

		public IReadOnlyList<TournamentYear> TournamentYears => YearsList;

		public IReadOnlyList<Division> Divisions => DivisionsList;

		public IReadOnlyList<Pool> Pools => PoolsList;

		public IReadOnlyList<Team> Teams => TeamsList;

		public IReadOnlyList<PoolPlayGame> PoolPlayGames => PoolPlayGamesList;

		public IReadOnlyList<ChampionshipGame> ChampionshipGames => ChampionshipGamesList;

		public IReadOnlyList<ShootoutRound> ShootoutRounds => ShootoutRoundsList;

		public IReadOnlyList<TiebreakerOverride> TiebreakerOverrides => OverridesList;

		public Task LoadAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task LoadTournamentYearAsync(int year, string competitionSlug, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task LoadDivisionDataAsync(int divisionId, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public DivisionStandingsSnapshotDocument? GetStandingsForDivision(int divisionId) =>
			StandingsMap.GetValueOrDefault(divisionId);

		public void SetStandingsForDivision(int divisionId, DivisionStandingsSnapshotDocument standings) =>
			StandingsMap[divisionId] = standings;

		public (int Year, string Slug)? GetTournamentPathForYear(int tournamentYearId)
		{
			var y = YearsList.FirstOrDefault(x => x.Id == tournamentYearId);
			return y is null ? null : (y.Year, "panther-shootout");
		}

		public string? GetDivisionKey(int divisionId)
		{
			var d = DivisionsList.FirstOrDefault(x => x.Id == divisionId);
			return d is null ? null : DivisionKeyHelper.ToKey(d.AgeGroup, d.Gender);
		}

		public int AllocateNextId() => _nextId++;

		public void Add<T>(T entity) where T : AuditableEntity
		{
			if (entity.Id == 0)
			{
				entity.Id = _nextId++;
			}

			switch (entity)
			{
				case AysoRegion r: RegionsList.Add(r); break;
				case TournamentYear y: YearsList.Add(y); break;
				case Division d: DivisionsList.Add(d); break;
				case Pool p: PoolsList.Add(p); break;
				case Team t:
					t.Pool = PoolsList.FirstOrDefault(p => p.Id == t.PoolId);
					TeamsList.Add(t);
					break;
				case PoolPlayGame g: PoolPlayGamesList.Add(g); break;
				case ChampionshipGame cg: ChampionshipGamesList.Add(cg); break;
				case ShootoutRound sr: ShootoutRoundsList.Add(sr); break;
				case TiebreakerOverride to: OverridesList.Add(to); break;
			}
		}

		public void Update<T>(T entity) where T : AuditableEntity
		{
		}

		public void Remove<T>(T entity) where T : AuditableEntity
		{
			switch (entity)
			{
				case Division d:
					var poolIds = PoolsList.Where(p => p.DivisionId == d.Id).Select(p => p.Id).ToHashSet();
					TeamsList.RemoveAll(t => poolIds.Contains(t.PoolId));
					PoolsList.RemoveAll(p => p.DivisionId == d.Id);
					PoolPlayGamesList.RemoveAll(g => g.DivisionId == d.Id);
					ChampionshipGamesList.RemoveAll(g => g.DivisionId == d.Id);
					ShootoutRoundsList.RemoveAll(r => r.DivisionId == d.Id);
					OverridesList.RemoveAll(o => o.DivisionId == d.Id);
					StandingsMap.Remove(d.Id);
					DivisionsList.RemoveAll(x => x.Id == d.Id);
					break;
			}
		}

		public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
		{
			if (ThrowOnSaveChanges)
			{
				throw new IOException("Simulated storage write failure.");
			}

			return Task.FromResult(1);
		}

		public Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
		{
			var savedDivisions = DivisionsList.ToList();
			var savedPools = PoolsList.ToList();
			var savedTeams = TeamsList.ToList();
			var savedPoolGames = PoolPlayGamesList.ToList();
			var savedChampGames = ChampionshipGamesList.ToList();
			var savedShootouts = ShootoutRoundsList.ToList();
			var savedStandings = new Dictionary<int, DivisionStandingsSnapshotDocument>(StandingsMap);

			return Task.FromResult<ITransaction>(new RollbackGuard(() =>
			{
				DivisionsList.Clear();
				DivisionsList.AddRange(savedDivisions);
				PoolsList.Clear();
				PoolsList.AddRange(savedPools);
				TeamsList.Clear();
				TeamsList.AddRange(savedTeams);
				PoolPlayGamesList.Clear();
				PoolPlayGamesList.AddRange(savedPoolGames);
				ChampionshipGamesList.Clear();
				ChampionshipGamesList.AddRange(savedChampGames);
				ShootoutRoundsList.Clear();
				ShootoutRoundsList.AddRange(savedShootouts);
				StandingsMap.Clear();
				foreach (var kvp in savedStandings)
				{
					StandingsMap[kvp.Key] = kvp.Value;
				}
			}));
		}

		private sealed class RollbackGuard(Action restore) : ITransaction
		{
			private bool _committed;

			public Task CommitAsync(CancellationToken cancellationToken = default)
			{
				_committed = true;
				return Task.CompletedTask;
			}

			public Task RollbackAsync(CancellationToken cancellationToken = default)
			{
				_committed = true;
				restore();
				return Task.CompletedTask;
			}

			public ValueTask DisposeAsync()
			{
				if (!_committed)
				{
					restore();
				}

				return ValueTask.CompletedTask;
			}
		}
	}
}
