namespace NorcusSheetsManager.SharedKernel;

/// <summary>
/// Process exit codes returned from <c>Program.Main</c>. Stable values that scripts and
/// service supervisors can branch on.
/// </summary>
public enum ExitCode
{
  Success = 0,
  GenericError = 1,
  ConfigurationError = 2,
  UnsupportedPlatform = 3,
  ServiceManagementFailed = 4,
  StartupFailed = 5,
}
