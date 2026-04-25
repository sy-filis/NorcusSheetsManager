using System.ComponentModel.DataAnnotations;
using ImageMagick;

namespace NorcusSheetsManager.Application.Configuration;

public class ConverterSettings : IValidatableObject
{
  [Required(AllowEmptyStrings = false, ErrorMessage = "SheetsPath is required.")]
  public string? SheetsPath { get; init; }

  public bool AutoScan { get; init; } = true;
  public MagickFormat OutFileFormat { get; init; } = MagickFormat.Png;

  [NotNullChar]
  public char MultiPageDelimiter { get; init; } = '-';

  [Range(1, uint.MaxValue, ErrorMessage = "MultiPageCounterLength must be at least 1.")]
  public uint MultiPageCounterLength { get; init; } = 3;

  public uint MultiPageInitNumber { get; init; } = 1;

  [Range(100, uint.MaxValue, ErrorMessage = "DPI must be at least 100.")]
  public uint DPI { get; init; } = 300;

  public bool TransparentBackground { get; init; } = false;
  public bool CropImage { get; init; } = true;
  public bool MovePdfToSubfolder { get; init; } = true;
  public string PdfSubfolder { get; init; } = "Archiv PDF";
  public bool FixGDriveNaming { get; init; } = true;

  [MinLength(1, ErrorMessage = "WatchedExtensions must contain at least one entry.")]
  public string[] WatchedExtensions { get; init; } = [".pdf", ".jpg", ".jpeg", ".png", ".gif"];

  public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
  {
    if (!WatchedExtensions.Any(e => string.Equals(e, ".pdf", StringComparison.OrdinalIgnoreCase)))
    {
      yield return new ValidationResult(
          "WatchedExtensions must contain \".pdf\" — the app is PDF-driven and cannot run without it.",
          [nameof(WatchedExtensions)]);
    }
    if (MovePdfToSubfolder && string.IsNullOrEmpty(PdfSubfolder))
    {
      yield return new ValidationResult(
          "PdfSubfolder is required when MovePdfToSubfolder is true.",
          [nameof(PdfSubfolder)]);
    }
  }
}
