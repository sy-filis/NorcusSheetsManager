using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace NorcusSheetsManager.Infrastructure.Configuration;

/// <summary>
/// Recursive DataAnnotations validator. Walks public instance properties of <paramref name="root"/>,
/// validates each with <see cref="Validator.TryValidateObject"/>, and recurses into complex
/// (non-leaf, non-collection) properties so nested config sections like <c>AppConfig.Converter</c>
/// get checked. Throws an aggregated <see cref="InvalidOperationException"/> when any rule fails.
/// </summary>
internal static class ConfigValidator
{
  public static void ValidateRecursive(object root)
  {
    var errors = new List<string>();
    _Validate(root, path: "", errors);
    if (errors.Count > 0)
    {
      throw new InvalidOperationException(
          "Configuration validation failed:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }
  }

  private static void _Validate(object obj, string path, List<string> errors)
  {
    var ctx = new ValidationContext(obj);
    var results = new List<ValidationResult>();
    Validator.TryValidateObject(obj, ctx, results, validateAllProperties: true);
    foreach (ValidationResult r in results)
    {
      string member = r.MemberNames.FirstOrDefault() ?? "";
      string fullPath = (path, member) switch
      {
        ("", "") => "(root)",
        ("", _) => member,
        (_, "") => path,
        _ => $"{path}.{member}",
      };
      errors.Add($"  {fullPath}: {r.ErrorMessage}");
    }

    foreach (PropertyInfo prop in obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
      if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
      {
        continue;
      }
      Type t = prop.PropertyType;
      if (_IsLeaf(t))
      {
        continue;
      }
      object? value = prop.GetValue(obj);
      if (value is null)
      {
        continue;
      }
      string nextPath = string.IsNullOrEmpty(path) ? prop.Name : $"{path}.{prop.Name}";
      _Validate(value, nextPath, errors);
    }
  }

  private static bool _IsLeaf(Type t)
  {
    Type underlying = Nullable.GetUnderlyingType(t) ?? t;
    return underlying.IsPrimitive
        || underlying.IsEnum
        || underlying == typeof(string)
        || underlying == typeof(decimal)
        || underlying == typeof(DateTime)
        || underlying == typeof(DateTimeOffset)
        || underlying == typeof(TimeSpan)
        || underlying == typeof(Guid)
        || typeof(IEnumerable).IsAssignableFrom(underlying);
  }
}
