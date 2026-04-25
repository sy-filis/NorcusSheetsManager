using System.ComponentModel.DataAnnotations;
using ImageMagick;

namespace NorcusSheetsManager.Application.Configuration;

public class ConverterSettings : IValidatableObject
{
  [Required(AllowEmptyStrings = false, ErrorMessage = "SheetsPath is required.")]
  public string? SheetsPath { get; set; }

  public bool AutoScan { get; set; } = true;
  public MagickFormat OutFileFormat { get; set; } = MagickFormat.Png;

  [NotNullChar]
  public char MultiPageDelimiter { get; set; } = '-';

  [Range(1, uint.MaxValue, ErrorMessage = "MultiPageCounterLength must be at least 1.")]
  public uint MultiPageCounterLength { get; set; } = 3;

  public uint MultiPageInitNumber { get; set; } = 1;

  [Range(100, uint.MaxValue, ErrorMessage = "DPI must be at least 100.")]
  public uint DPI { get; set; } = 300;

  public bool TransparentBackground { get; set; } = false;
  public bool CropImage { get; set; } = true;
  public bool MovePdfToSubfolder { get; set; } = true;
  public string PdfSubfolder { get; set; } = "Archiv PDF";
  public bool FixGDriveNaming { get; set; } = true;

  [MinLength(1, ErrorMessage = "WatchedExtensions must contain at least one entry.")]
  public string[] WatchedExtensions { get; set; } = [".pdf", ".jpg", ".jpeg", ".png", ".gif"];

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
