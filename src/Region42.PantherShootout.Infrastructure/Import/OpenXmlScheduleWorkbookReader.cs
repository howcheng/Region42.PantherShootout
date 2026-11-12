using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Region42.PantherShootout.Domain.Helpers;
using Region42.PantherShootout.Domain.Interfaces;
using Region42.PantherShootout.Domain.Models;
using Drawing = DocumentFormat.OpenXml.Drawing;

namespace Region42.PantherShootout.Infrastructure.Import;

/// <summary>
/// Reads the <c>Pools</c> and <c>Schedule</c> worksheets of an OpenXML (<c>.xlsx</c>) workbook,
/// extracting team blocks, dynamic color keys, field/date/time coordinates, and cell fills while
/// ignoring all other worksheets.
/// </summary>
public class OpenXmlScheduleWorkbookReader : IScheduleWorkbookReader
{
	private const string PoolsSheetName = "Pools";
	private const string ScheduleSheetName = "Schedule";
	private static readonly DateTime ExcelEpoch = new(1899, 12, 30);

	public ParsedWorkbookData ReadWorkbook(Stream workbookStream)
	{
		ArgumentNullException.ThrowIfNull(workbookStream);

		Stream seekableStream = workbookStream;
		MemoryStream? ownedBuffer = null;

		try
		{
			if (!workbookStream.CanSeek)
			{
				ownedBuffer = new MemoryStream();
				workbookStream.CopyTo(ownedBuffer);
				ownedBuffer.Position = 0;
				seekableStream = ownedBuffer;
			}
			else if (workbookStream.Position != 0)
			{
				workbookStream.Position = 0;
			}

			using var document = SpreadsheetDocument.Open(seekableStream, isEditable: false);
			var workbookPart = document.WorkbookPart;
			if (workbookPart?.Workbook?.Sheets is null)
			{
				return new ParsedWorkbookData
				{
					ReaderIssues =
					[
						new ParsedWorkbookIssue
						{
							SheetName = "Workbook",
							CellReference = string.Empty,
							Message = "The uploaded Excel workbook does not contain any worksheets.",
						},
					],
				};
			}

			var sharedStrings = ReadSharedStrings(workbookPart);
			var xfFillRgbs = ReadCellFormatFills(workbookPart);

			var poolsSheet = FindTargetSheet(workbookPart, PoolsSheetName);
			var scheduleSheet = FindTargetSheet(workbookPart, ScheduleSheetName);

			var issues = new List<ParsedWorkbookIssue>();
			var divisionBlocks = new List<ParsedDivisionPoolBlock>();
			var colorKeyEntries = new List<ParsedColorKeyEntry>();
			var scheduleCells = new List<ParsedScheduleCell>();

			if (poolsSheet?.Id?.Value is string poolsRelId &&
				workbookPart.GetPartById(poolsRelId) is WorksheetPart poolsPart)
			{
				var grid = ReadSheetGrid(poolsPart, sharedStrings, xfFillRgbs);
				ParsePoolsGrid(grid, divisionBlocks, issues);
			}

			if (scheduleSheet?.Id?.Value is string scheduleRelId &&
				workbookPart.GetPartById(scheduleRelId) is WorksheetPart schedulePart)
			{
				var grid = ReadSheetGrid(schedulePart, sharedStrings, xfFillRgbs);
				ParseScheduleGrid(grid, colorKeyEntries, scheduleCells, issues);
			}

			return new ParsedWorkbookData
			{
				HasPoolsSheet = poolsSheet is not null,
				HasScheduleSheet = scheduleSheet is not null,
				DivisionBlocks = divisionBlocks,
				ColorKeyEntries = colorKeyEntries,
				ScheduleCells = scheduleCells,
				ReaderIssues = issues,
			};
		}
		catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or FileFormatException or FormatException)
		{
			return new ParsedWorkbookData
			{
				ReaderIssues =
				[
					new ParsedWorkbookIssue
					{
						SheetName = "Workbook",
						CellReference = string.Empty,
						Message = $"The uploaded file is not a valid Excel (.xlsx) workbook: {ex.Message}",
					},
				],
			};
		}
		finally
		{
			ownedBuffer?.Dispose();
		}
	}

	private static Sheet? FindTargetSheet(WorkbookPart workbookPart, string expectedName)
	{
		var matches = workbookPart.Workbook!.Sheets!
			.Elements<Sheet>()
			.Where(s => string.Equals(s.Name?.Value?.Trim(), expectedName, StringComparison.OrdinalIgnoreCase))
			.ToList();

		if (matches.Count == 0)
		{
			return null;
		}

		// Prefer visible sheet if a workbook ever has both a visible and hidden copy with the same case-insensitive name.
		return matches.FirstOrDefault(s => s.State?.Value is null || s.State.Value == SheetStateValues.Visible)
			?? matches[0];
	}

	private static string[] ReadSharedStrings(WorkbookPart workbookPart)
	{
		var sst = workbookPart.SharedStringTablePart?.SharedStringTable;
		if (sst is null)
		{
			return [];
		}

		var items = sst.Elements<SharedStringItem>().ToList();
		var result = new string[items.Count];
		for (var i = 0; i < items.Count; i++)
		{
			result[i] = items[i].InnerText ?? string.Empty;
		}

		return result;
	}

	private static string?[] ReadCellFormatFills(WorkbookPart workbookPart)
	{
		var styles = workbookPart.WorkbookStylesPart?.Stylesheet;
		if (styles?.CellFormats is null || styles.Fills is null)
		{
			return [];
		}

		var themeRgbs = ReadThemePalette(workbookPart);
		var fills = styles.Fills.Elements<Fill>().ToList();
		var fillRgbs = new string?[fills.Count];

		for (var i = 0; i < fills.Count; i++)
		{
			var patternFill = fills[i].PatternFill;
			if (patternFill is null ||
				patternFill.PatternType?.Value == PatternValues.None ||
				patternFill.ForegroundColor is null)
			{
				continue;
			}

			fillRgbs[i] = ResolveColorTypeRgb(patternFill.ForegroundColor, themeRgbs);
		}

		var cellFormats = styles.CellFormats.Elements<CellFormat>().ToList();
		var xfFills = new string?[cellFormats.Count];
		for (var i = 0; i < cellFormats.Count; i++)
		{
			var fillId = (int)(cellFormats[i].FillId?.Value ?? 0U);
			if (fillId >= 0 && fillId < fillRgbs.Length)
			{
				xfFills[i] = fillRgbs[fillId];
			}
		}

		return xfFills;
	}

	private static string?[] ReadThemePalette(WorkbookPart workbookPart)
	{
		var scheme = workbookPart.ThemePart?.Theme?.ThemeElements?.ColorScheme;
		if (scheme is null)
		{
			return [];
		}

		// Standard OpenXML theme color index order: 0=lt1, 1=dk1, 2=lt2, 3=dk2, 4..9=accent1..6, 10=hlink, 11=folHlink
		return
		[
			ExtractThemeElementRgb(scheme.Light1Color),
			ExtractThemeElementRgb(scheme.Dark1Color),
			ExtractThemeElementRgb(scheme.Light2Color),
			ExtractThemeElementRgb(scheme.Dark2Color),
			ExtractThemeElementRgb(scheme.Accent1Color),
			ExtractThemeElementRgb(scheme.Accent2Color),
			ExtractThemeElementRgb(scheme.Accent3Color),
			ExtractThemeElementRgb(scheme.Accent4Color),
			ExtractThemeElementRgb(scheme.Accent5Color),
			ExtractThemeElementRgb(scheme.Accent6Color),
			ExtractThemeElementRgb(scheme.Hyperlink),
			ExtractThemeElementRgb(scheme.FollowedHyperlinkColor),
		];
	}

	private static string? ExtractThemeElementRgb(Drawing.Color2Type? colorElement)
	{
		if (colorElement is null)
		{
			return null;
		}

		if (colorElement.RgbColorModelHex?.Val?.Value is string rgbHex)
		{
			return NormalizeHexRgb(rgbHex);
		}

		if (colorElement.SystemColor?.LastColor?.Value is string sysHex)
		{
			return NormalizeHexRgb(sysHex);
		}

		return null;
	}

	private static string? ResolveColorTypeRgb(ColorType color, string?[] themePalette)
	{
		if (color.Rgb?.Value is string rawHex && !string.IsNullOrWhiteSpace(rawHex))
		{
			return NormalizeHexRgb(rawHex);
		}

		if (color.Theme?.Value is uint themeIdx && themeIdx < themePalette.Length && themePalette[themeIdx] is string baseHex)
		{
			var tint = color.Tint?.Value ?? 0.0;
			return Math.Abs(tint) < 0.0001 ? baseHex : ApplyExcelTint(baseHex, tint);
		}

		return null;
	}

	private static string NormalizeHexRgb(string hex)
	{
		var trimmed = hex.Trim().TrimStart('#').ToUpperInvariant();
		if (trimmed.Length == 8)
		{
			return trimmed[2..];
		}

		return trimmed;
	}

	private static string ApplyExcelTint(string hexRgb, double tint)
	{
		if (hexRgb.Length != 6 ||
			!int.TryParse(hexRgb[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
			!int.TryParse(hexRgb[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
			!int.TryParse(hexRgb[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
		{
			return hexRgb;
		}

		static int TintChannel(int channel, double t)
		{
			var adjusted = t < 0
				? channel * (1.0 + t)
				: (channel * (1.0 - t)) + (255.0 * t);
			return Math.Clamp((int)Math.Round(adjusted), 0, 255);
		}

		return $"{TintChannel(r, tint):X2}{TintChannel(g, tint):X2}{TintChannel(b, tint):X2}";
	}

	private static SheetGrid ReadSheetGrid(WorksheetPart worksheetPart, string[] sharedStrings, string?[] xfFills)
	{
		var grid = new SheetGrid();
		var sheetData = worksheetPart.Worksheet?.GetFirstChild<SheetData>();
		if (sheetData is null)
		{
			return grid;
		}

		var fallbackRow = 0;
		foreach (var row in sheetData.Elements<Row>())
		{
			var rowIndex = row.RowIndex?.Value is uint rIdx && rIdx > 0
				? (int)rIdx
				: fallbackRow + 1;
			fallbackRow = rowIndex;

			var fallbackCol = 0;
			foreach (var cell in row.Elements<Cell>())
			{
				var cellRef = cell.CellReference?.Value;
				var colIndex = TryParseColumnIndex(cellRef, out var parsedCol)
					? parsedCol
					: fallbackCol + 1;
				fallbackCol = colIndex;

				var resolvedRef = string.IsNullOrWhiteSpace(cellRef)
					? $"{ToColumnLetters(colIndex)}{rowIndex}"
					: cellRef.ToUpperInvariant();

				var (text, numericValue) = ReadCellContent(cell, sharedStrings);
				string? fillRgb = null;
				if (cell.StyleIndex?.Value is uint styleIdx && styleIdx < xfFills.Length)
				{
					fillRgb = xfFills[styleIdx];
				}

				grid.Set(rowIndex, colIndex, new GridCell(resolvedRef, rowIndex, colIndex, text, numericValue, fillRgb));
			}
		}

		return grid;
	}

	private static (string Text, double? NumericValue) ReadCellContent(Cell cell, string[] sharedStrings)
	{
		var dataType = cell.DataType?.Value;
		if (dataType == CellValues.SharedString)
		{
			if (int.TryParse(cell.CellValue?.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ssIdx) &&
				ssIdx >= 0 && ssIdx < sharedStrings.Length)
			{
				return (sharedStrings[ssIdx], null);
			}

			return (string.Empty, null);
		}

		if (dataType == CellValues.InlineString)
		{
			return (cell.InlineString?.InnerText ?? string.Empty, null);
		}

		if (dataType == CellValues.String || dataType == CellValues.SharedString)
		{
			return (cell.CellValue?.InnerText ?? string.Empty, null);
		}

		if (dataType == CellValues.Boolean)
		{
			var rawBool = cell.CellValue?.InnerText;
			return (rawBool == "1" ? "TRUE" : "FALSE", null);
		}

		var raw = cell.CellValue?.InnerText ?? cell.InnerText ?? string.Empty;
		if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
		{
			return (raw, num);
		}

		return (raw, null);
	}

	private static void ParsePoolsGrid(
		SheetGrid grid,
		List<ParsedDivisionPoolBlock> divisionBlocks,
		List<ParsedWorkbookIssue> issues)
	{
		var rowNumbers = grid.RowNumbers;
		var idx = 0;

		while (idx < rowNumbers.Count)
		{
			var rowNum = rowNumbers[idx];
			var rowCells = grid.GetRow(rowNum);
			var divisionHeaders = rowCells
				.Where(c => IsStandaloneDivisionHeader(c.Text))
				.OrderBy(c => c.ColumnIndex)
				.ToList();

			if (divisionHeaders.Count == 0)
			{
				idx++;
				continue;
			}

			var subHeaderRowNum = rowNum + 1;
			var subHeaderCells = grid.GetRowByIndex(subHeaderRowNum);

			// Determine where team rows end for this band of side-by-side division blocks.
			var endRowNum = rowNum + 1;
			var scanIdx = idx + 1;
			while (scanIdx < rowNumbers.Count)
			{
				var candidateRow = rowNumbers[scanIdx];
				if (candidateRow == subHeaderRowNum)
				{
					endRowNum = candidateRow;
					scanIdx++;
					continue;
				}

				// Stop if there was a blank gap row or another division header row.
				if (candidateRow > endRowNum + 1)
				{
					break;
				}

				var candidateCells = grid.GetRow(candidateRow);
				if (candidateCells.Any(c => IsStandaloneDivisionHeader(c.Text)))
				{
					break;
				}

				if (candidateCells.All(c => string.IsNullOrWhiteSpace(c.Text)))
				{
					break;
				}

				endRowNum = candidateRow;
				scanIdx++;
			}

			for (var h = 0; h < divisionHeaders.Count; h++)
			{
				var headerCell = divisionHeaders[h];
				var spanStartCol = Math.Max(1, headerCell.ColumnIndex - 1);
				var spanEndCol = h + 1 < divisionHeaders.Count
					? Math.Max(headerCell.ColumnIndex, divisionHeaders[h + 1].ColumnIndex - 2)
					: int.MaxValue;

				var fillRgb = FindHeaderFillRgb(rowCells, headerCell, spanStartCol, spanEndCol);
				var poolColumns = DiscoverPoolColumnPairs(subHeaderCells, spanStartCol, spanEndCol);

				if (poolColumns.Count == 0)
				{
					issues.Add(new ParsedWorkbookIssue
					{
						SheetName = PoolsSheetName,
						CellReference = headerCell.CellReference,
						Message = $"Division block '{headerCell.Text.Trim()}' has no '# / Pool' column headers beneath it.",
					});
					continue;
				}

				var parsedPools = new List<ParsedPoolColumn>();
				for (var p = 0; p < poolColumns.Count; p++)
				{
					var (codeCol, teamCol, poolHeaderText) = poolColumns[p];
					var teamRows = new List<ParsedPoolTeamRow>();

					for (var r = subHeaderRowNum + 1; r <= endRowNum; r++)
					{
						var codeCell = grid.GetCell(r, codeCol);
						var teamCell = grid.GetCell(r, teamCol);
						var rawCode = codeCell?.Text.Trim() ?? string.Empty;
						var rawTeam = teamCell?.Text.Trim() ?? string.Empty;

						if (string.IsNullOrWhiteSpace(rawCode) && string.IsNullOrWhiteSpace(rawTeam))
						{
							continue;
						}

						teamRows.Add(new ParsedPoolTeamRow
						{
							CodeCellReference = codeCell?.CellReference ?? $"{ToColumnLetters(codeCol)}{r}",
							RawCode = rawCode,
							TeamCellReference = teamCell?.CellReference ?? $"{ToColumnLetters(teamCol)}{r}",
							RawTeamToken = rawTeam,
						});
					}

					if (teamRows.Count > 0)
					{
						parsedPools.Add(new ParsedPoolColumn
						{
							PoolColumnOrdinal = p + 1,
							HeaderText = poolHeaderText,
							Teams = teamRows,
						});
					}
				}

				divisionBlocks.Add(new ParsedDivisionPoolBlock
				{
					HeaderCellReference = headerCell.CellReference,
					DivisionHeaderText = headerCell.Text.Trim(),
					HeaderFillRgb = fillRgb,
					Pools = parsedPools,
				});
			}

			idx = scanIdx;
		}
	}

	private static string? FindHeaderFillRgb(
		IReadOnlyList<GridCell> rowCells,
		GridCell headerCell,
		int spanStartCol,
		int spanEndCol)
	{
		if (IsUsableColorFill(headerCell.FillRgb))
		{
			return headerCell.FillRgb;
		}

		return rowCells
			.Where(c => c.ColumnIndex >= spanStartCol && c.ColumnIndex <= spanEndCol && IsUsableColorFill(c.FillRgb))
			.Select(c => c.FillRgb)
			.FirstOrDefault();
	}

	private static List<(int CodeCol, int TeamCol, string HeaderText)> DiscoverPoolColumnPairs(
		IReadOnlyDictionary<int, GridCell> subHeaderCells,
		int spanStartCol,
		int spanEndCol)
	{
		var pairs = new List<(int CodeCol, int TeamCol, string HeaderText)>();

		foreach (var cell in subHeaderCells.Values.OrderBy(c => c.ColumnIndex))
		{
			if (cell.ColumnIndex < spanStartCol || cell.ColumnIndex > spanEndCol)
			{
				continue;
			}

			var trimmed = cell.Text.Trim();
			if (string.Equals(trimmed, "#", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(trimmed, "Code", StringComparison.OrdinalIgnoreCase))
			{
				var nextCol = cell.ColumnIndex + 1;
				var poolHeader = subHeaderCells.TryGetValue(nextCol, out var nextCell)
					? nextCell.Text.Trim()
					: $"Pool {pairs.Count + 1}";
				pairs.Add((cell.ColumnIndex, nextCol, poolHeader));
			}
		}

		if (pairs.Count == 0)
		{
			foreach (var cell in subHeaderCells.Values.OrderBy(c => c.ColumnIndex))
			{
				if (cell.ColumnIndex < spanStartCol || cell.ColumnIndex > spanEndCol)
				{
					continue;
				}

				var trimmed = cell.Text.Trim();
				if (trimmed.StartsWith("Pool", StringComparison.OrdinalIgnoreCase) && cell.ColumnIndex > spanStartCol)
				{
					pairs.Add((cell.ColumnIndex - 1, cell.ColumnIndex, trimmed));
				}
			}
		}

		return pairs;
	}

	private static void ParseScheduleGrid(
		SheetGrid grid,
		List<ParsedColorKeyEntry> colorKeyEntries,
		List<ParsedScheduleCell> scheduleCells,
		List<ParsedWorkbookIssue> issues)
	{
		var legendCoords = ExtractColorKeyEntries(grid, colorKeyEntries);

		var fieldByColumn = new Dictionary<int, string>();
		var ageHintByColumn = new Dictionary<int, string>();
		var skippedRows = new HashSet<int>();
		DateTime? currentDate = null;

		foreach (var rowNum in grid.RowNumbers)
		{
			if (skippedRows.Contains(rowNum))
			{
				continue;
			}

			var rowCells = grid.GetRowByIndex(rowNum);
			rowCells.TryGetValue(1, out var colACell);

			if (colACell is not null && TryParseExcelDate(colACell, out var parsedDate))
			{
				currentDate = parsedDate.Date;

				foreach (var cell in rowCells.Values.Where(c => c.ColumnIndex >= 2).OrderBy(c => c.ColumnIndex))
				{
					if (legendCoords.Contains((cell.RowIndex, cell.ColumnIndex)))
					{
						continue;
					}

					var headerText = cell.Text.Trim();
					if (!string.IsNullOrWhiteSpace(headerText))
					{
						fieldByColumn[cell.ColumnIndex] = headerText;
					}
				}

				// Inspect immediate subheader row (e.g. "B&G 12U" age-group hints or secondary Field labels).
				var subRowNum = rowNum + 1;
				var subRowCells = grid.GetRowByIndex(subRowNum);
				if (subRowCells.Count > 0)
				{
					subRowCells.TryGetValue(1, out var subColA);
					var subIsDateOrTime = subColA is not null &&
						(TryParseExcelDate(subColA, out _) || TryParseExcelTime(subColA, out _));

					if (!subIsDateOrTime)
					{
						foreach (var subCell in subRowCells.Values.Where(c => c.ColumnIndex >= 2))
						{
							if (legendCoords.Contains((subCell.RowIndex, subCell.ColumnIndex)))
							{
								continue;
							}

							var subText = subCell.Text.Trim();
							if (string.IsNullOrWhiteSpace(subText))
							{
								continue;
							}

							if (subText.StartsWith("Field", StringComparison.OrdinalIgnoreCase))
							{
								fieldByColumn[subCell.ColumnIndex] = subText;
							}
							else
							{
								ageHintByColumn[subCell.ColumnIndex] = subText;
							}
						}

						skippedRows.Add(subRowNum);
					}
				}

				continue;
			}

			if (colACell is not null && TryParseExcelTime(colACell, out var timeOfDay))
			{
				if (currentDate is null)
				{
					issues.Add(new ParsedWorkbookIssue
					{
						SheetName = ScheduleSheetName,
						CellReference = colACell.CellReference,
						Message = "Schedule time row appears before any tournament date header row.",
					});
					continue;
				}

				var scheduledDateTime = currentDate.Value.Add(timeOfDay);

				foreach (var cell in rowCells.Values.Where(c => c.ColumnIndex >= 2).OrderBy(c => c.ColumnIndex))
				{
					if (legendCoords.Contains((cell.RowIndex, cell.ColumnIndex)))
					{
						continue;
					}

					var text = cell.Text.Trim();
					if (string.IsNullOrWhiteSpace(text))
					{
						continue;
					}

					if (!fieldByColumn.TryGetValue(cell.ColumnIndex, out var location) || string.IsNullOrWhiteSpace(location))
					{
						issues.Add(new ParsedWorkbookIssue
						{
							SheetName = ScheduleSheetName,
							CellReference = cell.CellReference,
							Message = $"Schedule entry '{text}' is in a column with no Field header.",
						});
						continue;
					}

					ageHintByColumn.TryGetValue(cell.ColumnIndex, out var ageHint);

					scheduleCells.Add(new ParsedScheduleCell
					{
						CellReference = cell.CellReference,
						ScheduledDateTime = scheduledDateTime,
						Location = location,
						ColumnAgeHint = ageHint,
						RawText = text,
						FillRgb = IsUsableColorFill(cell.FillRgb) ? cell.FillRgb : null,
					});
				}
			}
		}
	}

	private static HashSet<(int Row, int Col)> ExtractColorKeyEntries(
		SheetGrid grid,
		List<ParsedColorKeyEntry> colorKeyEntries)
	{
		var legendCoords = new HashSet<(int Row, int Col)>();

		foreach (var cell in grid.AllCells)
		{
			var text = cell.Text.Trim();
			if (!string.Equals(text, "Color Key", StringComparison.OrdinalIgnoreCase) &&
				!string.Equals(text, "Legend", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			legendCoords.Add((cell.RowIndex, cell.ColumnIndex));
			for (var r = cell.RowIndex + 1; r <= grid.MaxRow; r++)
			{
				var entryCell = grid.GetCell(r, cell.ColumnIndex);
				if (entryCell is null || string.IsNullOrWhiteSpace(entryCell.Text))
				{
					break;
				}

				legendCoords.Add((r, cell.ColumnIndex));
				colorKeyEntries.Add(new ParsedColorKeyEntry
				{
					CellReference = entryCell.CellReference,
					Label = entryCell.Text.Trim(),
					FillRgb = IsUsableColorFill(entryCell.FillRgb) ? entryCell.FillRgb : null,
				});
			}
		}

		// Also capture any standalone division-code cells outside Field columns that carry a color fill.
		if (colorKeyEntries.Count == 0)
		{
			foreach (var cell in grid.AllCells.Where(c => c.ColumnIndex >= 2))
			{
				if (IsStandaloneDivisionHeader(cell.Text) && IsUsableColorFill(cell.FillRgb))
				{
					legendCoords.Add((cell.RowIndex, cell.ColumnIndex));
					colorKeyEntries.Add(new ParsedColorKeyEntry
					{
						CellReference = cell.CellReference,
						Label = cell.Text.Trim(),
						FillRgb = cell.FillRgb,
					});
				}
			}
		}

		return legendCoords;
	}

	private static bool IsStandaloneDivisionHeader(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		var trimmed = text.Trim();
		// Exclude team tokens such as "10UG-121-Wright" which have 2+ hyphens.
		if (trimmed.Count(ch => ch == '-') >= 2)
		{
			return false;
		}

		try
		{
			_ = DivisionKeyHelper.ParseDivisionName(trimmed);
			return true;
		}
		catch (ArgumentException)
		{
			return false;
		}
	}

	private static bool IsUsableColorFill(string? fillRgb)
	{
		if (string.IsNullOrWhiteSpace(fillRgb))
		{
			return false;
		}

		return !string.Equals(fillRgb, "FFFFFF", StringComparison.OrdinalIgnoreCase) &&
			!string.Equals(fillRgb, "F8F8F8", StringComparison.OrdinalIgnoreCase) &&
			!string.Equals(fillRgb, "000000", StringComparison.OrdinalIgnoreCase);
	}

	private static bool TryParseExcelDate(GridCell cell, out DateTime date)
	{
		if (cell.NumericValue is double oaDate && oaDate >= 30000.0 && oaDate < 80000.0)
		{
			date = ExcelEpoch.AddDays(Math.Floor(oaDate));
			return true;
		}

		var text = cell.Text.Trim();
		if (!string.IsNullOrWhiteSpace(text) &&
			!text.Contains(':') &&
			DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed) &&
			parsed.Year >= 2000)
		{
			date = parsed.Date;
			return true;
		}

		date = default;
		return false;
	}

	private static bool TryParseExcelTime(GridCell cell, out TimeSpan timeOfDay)
	{
		if (cell.NumericValue is double numeric)
		{
			if (numeric >= 0.0 && numeric < 1.0)
			{
				timeOfDay = RoundFractionOfDay(numeric);
				return true;
			}

			if (numeric >= 30000.0 && numeric < 80000.0)
			{
				var fraction = numeric - Math.Floor(numeric);
				if (fraction > 0.0)
				{
					timeOfDay = RoundFractionOfDay(fraction);
					return true;
				}
			}
		}

		var text = cell.Text.Trim();
		if (!string.IsNullOrWhiteSpace(text) &&
			(text.Contains(':') || text.EndsWith("AM", StringComparison.OrdinalIgnoreCase) || text.EndsWith("PM", StringComparison.OrdinalIgnoreCase)) &&
			DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.NoCurrentDateDefault, out var parsedTime))
		{
			timeOfDay = parsedTime.TimeOfDay;
			return true;
		}

		timeOfDay = default;
		return false;
	}

	private static TimeSpan RoundFractionOfDay(double fraction)
	{
		var totalSeconds = (int)Math.Round(fraction * 86400.0);
		return TimeSpan.FromSeconds(totalSeconds);
	}

	private static bool TryParseColumnIndex(string? cellReference, out int columnIndex)
	{
		columnIndex = 0;
		if (string.IsNullOrWhiteSpace(cellReference))
		{
			return false;
		}

		var col = 0;
		var hasLetters = false;
		foreach (var ch in cellReference)
		{
			if (ch is >= 'A' and <= 'Z')
			{
				col = (col * 26) + (ch - 'A' + 1);
				hasLetters = true;
			}
			else if (ch is >= 'a' and <= 'z')
			{
				col = (col * 26) + (ch - 'a' + 1);
				hasLetters = true;
			}
			else
			{
				break;
			}
		}

		columnIndex = col;
		return hasLetters;
	}

	private static string ToColumnLetters(int columnIndex)
	{
		var dividend = columnIndex;
		var chars = new Stack<char>();
		while (dividend > 0)
		{
			var modulo = (dividend - 1) % 26;
			chars.Push((char)('A' + modulo));
			dividend = (dividend - modulo) / 26;
		}

		return new string(chars.ToArray());
	}

	private sealed record GridCell(
		string CellReference,
		int RowIndex,
		int ColumnIndex,
		string Text,
		double? NumericValue,
		string? FillRgb);

	private sealed class SheetGrid
	{
		private readonly SortedDictionary<int, Dictionary<int, GridCell>> _rows = new();

		public int MaxRow { get; private set; }

		public IReadOnlyList<int> RowNumbers => _rows.Keys.ToList();

		public IEnumerable<GridCell> AllCells =>
			_rows.Values.SelectMany(r => r.Values.OrderBy(c => c.ColumnIndex));

		public void Set(int rowIndex, int columnIndex, GridCell cell)
		{
			if (!_rows.TryGetValue(rowIndex, out var rowDict))
			{
				rowDict = new Dictionary<int, GridCell>();
				_rows[rowIndex] = rowDict;
			}

			rowDict[columnIndex] = cell;
			if (rowIndex > MaxRow)
			{
				MaxRow = rowIndex;
			}
		}

		public GridCell? GetCell(int rowIndex, int columnIndex)
		{
			return _rows.TryGetValue(rowIndex, out var row) && row.TryGetValue(columnIndex, out var cell)
				? cell
				: null;
		}

		public IReadOnlyList<GridCell> GetRow(int rowIndex)
		{
			return _rows.TryGetValue(rowIndex, out var row)
				? row.Values.OrderBy(c => c.ColumnIndex).ToList()
				: [];
		}

		public IReadOnlyDictionary<int, GridCell> GetRowByIndex(int rowIndex)
		{
			return _rows.TryGetValue(rowIndex, out var row)
				? row
				: new Dictionary<int, GridCell>();
		}
	}
}
