using System.ComponentModel.DataAnnotations;

namespace NorcusSheetsManager.Application.Configuration;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class NotNullCharAttribute : ValidationAttribute
{
  protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
  {
    if (value is char and '\0')
    {
      return new ValidationResult(
        $"{validationContext.DisplayName} must not be the null character.",
        [validationContext.MemberName ?? validationContext.DisplayName]);
    }
    return ValidationResult.Success;
  }
}
