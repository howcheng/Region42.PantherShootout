using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Options;
using Moq;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;
using Region42.PantherShootout.Domain.Interfaces;
using Region42.PantherShootout.Infrastructure.Import;
using Region42.PantherShootout.Infrastructure.Repositories;
using Region42.PantherShootout.Infrastructure.Storage;
using Xunit;

namespace Region42.PantherShootout.Infrastructure.Tests.Import;

public sealed class RegionsAndWorkbookReaderTests : IDisposable
{
	private readonly string _tempRoot;
	private readonly LocalFileTournamentDataStore _store;
	private readonly IOptions<StorageOptions> _storageOptions;

	public RegionsAndWorkbookReaderTests()
	{
		_tempRoot = Path.Combine(Path.GetTempPath(), "region42-pso-import-tests", Guid.NewGuid().ToString("N"));
		_storageOptions = Options.Create(new StorageOptions
		{
			Provider = "LocalFile",
			LocalRoot = _tempRoot,
			CompetitionSlug = "panther-shootout",
		});
		_store = new LocalFileTournamentDataStore(_storageOptions);
	}

	public void Dispose()
	{
		if (Directory.Exists(_tempRoot))
		{
			Directory.Delete(_tempRoot, recursive: true);
		}
	}

	[Fact]
	public async Task RegionsJson_PersistsIndependentlyOfYear_RewiresTeamDisplayNames_AndSupportsRollback()
	{
		var userService = Mock.Of<ICurrentUserService>(u => u.UserIdentifier == "admin@aysoregion42.org");
		var context1 = new TournamentDataContext(_store, _storageOptions, userService);
		var regionRepo1 = new Repository<AysoRegion>(context1);

		// Default regions hydrate in-memory without marking regions.json dirty on read.
		var initialRegions = await regionRepo1.GetAllAsync();
		Assert.Contains(initialRegions, r => r.RegionNumber == 42 && r.Name == "Newbury Park");
		Assert.Contains(initialRegions, r => r.RegionNumber == 121 && r.Name == "Simi Valley");
		Assert.Equal(0, await regionRepo1.SaveChangesAsync());

		// Add a new region (76 = Beverly Hills) and a team referencing it.
		var region76 = new AysoRegion { RegionNumber = 76, Name = "Beverly Hills" };
		await regionRepo1.AddAsync(region76);

		var year = new TournamentYear { Year = 2026, Name = "2026 Panther Shootout", IsActive = true };
		context1.Add(year);

		var division = new Division
		{
			TournamentYearId = year.Id,
			AgeGroup = AgeGroup.U10,
			Gender = Gender.Girls,
			Name = "10U Girls",
			Size = DivisionSize.Four,
		};
		context1.Add(division);

		var pool = new Pool { DivisionId = division.Id, Name = "A" };
		context1.Add(pool);

		var team = new Team
		{
			PoolId = pool.Id,
			TeamNumber = 1,
			RegionNumber = 76,
			CoachName = "Goodman",
		};
		context1.Add(team);

		Assert.Equal("Beverly Hills", team.RegionName);
		Assert.Equal("Beverly Hills: Goodman", team.Name);

		var savedFiles = await context1.SaveChangesAsync();
		Assert.Equal(3, savedFiles); // index.json + regions.json + tournament.json

		// Rehydrate in fresh context and rename region 76 -> team display name updates live!
		var context2 = new TournamentDataContext(_store, _storageOptions, userService);
		await context2.LoadAllAsync();

		var loadedRegion76 = context2.Regions.Single(r => r.RegionNumber == 76);
		var loadedTeam = context2.Teams.Single(t => t.Id == team.Id);
		Assert.Equal("Beverly Hills: Goodman", loadedTeam.Name);

		// Verify transaction rollback restores region state.
		await using (var tx = await context2.BeginTransactionAsync())
		{
			loadedRegion76.Name = "Rolled Back Name";
			context2.Update(loadedRegion76);
			Assert.Equal("Rolled Back Name: Goodman", loadedTeam.Name);
			await tx.RollbackAsync();
		}

		loadedTeam = context2.Teams.Single(t => t.Id == team.Id);
		Assert.Equal("Beverly Hills: Goodman", loadedTeam.Name);
		Assert.Equal(0, await context2.SaveChangesAsync());

		// Now commit a region rename and verify persistence to regions.json only (1 file).
		var regionToRename = context2.Regions.Single(r => r.RegionNumber == 76);
		regionToRename.Name = "Beverly Hills AYSO";
		context2.Update(regionToRename);
		Assert.Equal("Beverly Hills AYSO: Goodman", context2.Teams.Single(t => t.Id == team.Id).Name);
		Assert.Equal(1, await context2.SaveChangesAsync());
	}

	[Fact]
	public void OpenXmlReader_IgnoresHiddenAndVisibleJunkTabs_AndExtractsInheritedFieldHeadersAndFills()
	{
		using var workbookStream = BuildSyntheticWorkbookStream();
		var reader = new OpenXmlScheduleWorkbookReader();

		var parsed = reader.ReadWorkbook(workbookStream);

		Assert.True(parsed.HasPoolsSheet);
		Assert.True(parsed.HasScheduleSheet);
		Assert.Empty(parsed.ReaderIssues);

		var divBlock = Assert.Single(parsed.DivisionBlocks);
		Assert.Equal("12U Boys", divBlock.DivisionHeaderText);
		Assert.Equal("BDD6EE", divBlock.HeaderFillRgb);
		var poolCol = Assert.Single(divBlock.Pools);
		Assert.Equal(4, poolCol.Teams.Count);
		Assert.Equal("G1", poolCol.Teams[0].RawCode);
		Assert.Equal("12UB-42-Hitt", poolCol.Teams[0].RawTeamToken);

		var legend = Assert.Single(parsed.ColorKeyEntries);
		Assert.Equal("12UB", legend.Label);
		Assert.Equal("BDD6EE", legend.FillRgb);

		// 6 pool games + 2 championship games; nothing from "JunkNotes" or hidden "12UB" sheet!
		Assert.Equal(8, parsed.ScheduleCells.Count);
		var day2Champ = parsed.ScheduleCells.Single(c => c.RawText == "Champ");
		Assert.Equal("Field 1", day2Champ.Location); // Inherited from Day 1 B2 header!
		Assert.Equal("BDD6EE", day2Champ.FillRgb);
		Assert.Equal(new DateTime(2025, 11, 29, 14, 30, 0), day2Champ.ScheduledDateTime);
	}

	[Fact]
	public void OpenXmlReader_ReadsReal2025PantherShootoutWorkbookCleanly()
	{
		var workbookPath = FindSample2025WorkbookPath();
		Assert.True(File.Exists(workbookPath), $"Expected sample workbook at {workbookPath}");

		using var stream = File.OpenRead(workbookPath);
		var reader = new OpenXmlScheduleWorkbookReader();
		var parsed = reader.ReadWorkbook(stream);

		Assert.True(parsed.HasPoolsSheet);
		Assert.True(parsed.HasScheduleSheet);
		Assert.Empty(parsed.ReaderIssues);

		Assert.Equal(6, parsed.DivisionBlocks.Count);
		var totalTeams = parsed.DivisionBlocks.Sum(b => b.Pools.Sum(p => p.Teams.Count));
		Assert.Equal(38, totalTeams);

		Assert.Equal(6, parsed.ColorKeyEntries.Count);
		Assert.Equal(69, parsed.ScheduleCells.Count); // 48 pool-play + 21 bracket cells
	}

	private static string FindSample2025WorkbookPath()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null)
		{
			var candidate = Path.Combine(dir.FullName, "2025 Panther Shootout Schedule.xlsx");
			if (File.Exists(candidate))
			{
				return candidate;
			}

			dir = dir.Parent;
		}

		return Path.Combine(AppContext.BaseDirectory, "2025 Panther Shootout Schedule.xlsx");
	}

	private static MemoryStream BuildSyntheticWorkbookStream()
	{
		var ms = new MemoryStream();
		using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook, autoSave: true))
		{
			var wbPart = doc.AddWorkbookPart();
			wbPart.Workbook = new Workbook();

			var stylesPart = wbPart.AddNewPart<WorkbookStylesPart>();
			stylesPart.Stylesheet = new Stylesheet(
				new Fonts(new Font()),
				new Fills(
					new Fill(new PatternFill { PatternType = PatternValues.None }),
					new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
					new Fill(new PatternFill(new ForegroundColor { Rgb = "FFBDD6EE" }) { PatternType = PatternValues.Solid })),
				new Borders(new Border()),
				new CellFormats(
					new CellFormat { FillId = 0 },
					new CellFormat { FillId = 2, ApplyFill = true }));

			var sheets = wbPart.Workbook.AppendChild(new Sheets());

			// 1. Visible junk tab (must be ignored)
			var junkPart = wbPart.AddNewPart<WorksheetPart>();
			junkPart.Worksheet = new Worksheet(new SheetData(
				new Row(new Cell { CellReference = "A1", DataType = CellValues.InlineString, InlineString = new InlineString(new Text("Junk Tab Data")) }) { RowIndex = 1 }));
			sheets.Append(new Sheet { Id = wbPart.GetIdOfPart(junkPart), SheetId = 1, Name = "JunkNotes" });

			// 2. Hidden junk division tab (must be ignored)
			var hiddenPart = wbPart.AddNewPart<WorksheetPart>();
			hiddenPart.Worksheet = new Worksheet(new SheetData(
				new Row(new Cell { CellReference = "A1", DataType = CellValues.InlineString, InlineString = new InlineString(new Text("Old 2024 Scratch")) }) { RowIndex = 1 }));
			sheets.Append(new Sheet { Id = wbPart.GetIdOfPart(hiddenPart), SheetId = 2, Name = "12UB", State = SheetStateValues.Hidden });

			// 3. Pools tab
			var poolsPart = wbPart.AddNewPart<WorksheetPart>();
			poolsPart.Worksheet = new Worksheet(new SheetData(
				new Row(MakeInlineCell("B1", "12U Boys", styleIndex: 1)) { RowIndex = 1 },
				new Row(MakeInlineCell("A2", "#"), MakeInlineCell("B2", "Pool 1")) { RowIndex = 2 },
				new Row(MakeInlineCell("A3", "G1"), MakeInlineCell("B3", "12UB-42-Hitt")) { RowIndex = 3 },
				new Row(MakeInlineCell("A4", "G2"), MakeInlineCell("B4", "12UB-20-Ekstein")) { RowIndex = 4 },
				new Row(MakeInlineCell("A5", "G3"), MakeInlineCell("B5", "12UB-2-Cacho")) { RowIndex = 5 },
				new Row(MakeInlineCell("A6", "G4"), MakeInlineCell("B6", "12UB-4-Edwards")) { RowIndex = 6 }));
			sheets.Append(new Sheet { Id = wbPart.GetIdOfPart(poolsPart), SheetId = 3, Name = "Pools" });

			// 4. Schedule tab (Day 1 has Field 1 & Field 2 headers; Day 2 omits Field 1 & Field 2 to test inheritance)
			var day1Serial = (new DateTime(2025, 11, 28) - new DateTime(1899, 12, 30)).TotalDays;
			var day2Serial = (new DateTime(2025, 11, 29) - new DateTime(1899, 12, 30)).TotalDays;

			var schedulePart = wbPart.AddNewPart<WorksheetPart>();
			schedulePart.Worksheet = new Worksheet(new SheetData(
				new Row(
					MakeNumberCell("A2", day1Serial),
					MakeInlineCell("B2", "Field 1"),
					MakeInlineCell("C2", "Field 2")) { RowIndex = 2 },
				new Row(
					MakeNumberCell("A4", 8.0 / 24.0),
					MakeInlineCell("B4", "G1 vs G2", styleIndex: 1),
					MakeInlineCell("C4", "G3 vs G4", styleIndex: 1),
					MakeInlineCell("J4", "Color Key")) { RowIndex = 4 },
				new Row(
					MakeNumberCell("A5", 10.0 / 24.0),
					MakeInlineCell("B5", "G1 vs G3", styleIndex: 1),
					MakeInlineCell("C5", "G2 vs G4", styleIndex: 1),
					MakeInlineCell("J5", "12UB", styleIndex: 1)) { RowIndex = 5 },
				new Row(
					MakeNumberCell("A10", day2Serial)) { RowIndex = 10 },
				new Row(
					MakeNumberCell("A11", 10.0 / 24.0),
					MakeInlineCell("B11", "G1 vs G4", styleIndex: 1),
					MakeInlineCell("C11", "G2 vs G3", styleIndex: 1)) { RowIndex = 11 },
				new Row(
					MakeNumberCell("A12", 14.5 / 24.0),
					MakeInlineCell("B12", "Champ", styleIndex: 1),
					MakeInlineCell("C12", "Consol", styleIndex: 1)) { RowIndex = 12 }));
			sheets.Append(new Sheet { Id = wbPart.GetIdOfPart(schedulePart), SheetId = 4, Name = "Schedule" });
		}

		ms.Position = 0;
		return ms;
	}

	private static Cell MakeInlineCell(string cellRef, string text, uint styleIndex = 0) => new()
	{
		CellReference = cellRef,
		StyleIndex = styleIndex,
		DataType = CellValues.InlineString,
		InlineString = new InlineString(new Text(text)),
	};

	private static Cell MakeNumberCell(string cellRef, double value) => new()
	{
		CellReference = cellRef,
		DataType = CellValues.Number,
		CellValue = new CellValue(value.ToString("R", CultureInfo.InvariantCulture)),
	};
}
