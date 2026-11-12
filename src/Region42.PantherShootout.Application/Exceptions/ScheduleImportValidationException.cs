using Region42.PantherShootout.Application.DTOs;

namespace Region42.PantherShootout.Application.Exceptions;

/// <summary>
/// Thrown when confirming a schedule import whose workbook fails structural or business-rule validation.
/// </summary>
public class ScheduleImportValidationException : InvalidOperationException
{
	public ScheduleImportValidationException(IReadOnlyList<ScheduleImportValidationErrorDto> errors)
		: base($"Schedule workbook failed validation with {errors?.Count ?? 0} error(s).")
	{
		ArgumentNullException.ThrowIfNull(errors);
		Errors = errors;
	}

	public IReadOnlyList<ScheduleImportValidationErrorDto> Errors { get; }
}
