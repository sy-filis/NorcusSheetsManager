using NorcusSheetsManager.SharedKernel;

namespace NorcusSheetsManager.Application.Corrector;

public static class CorrectorErrors
{
  public static readonly Error Unauthorized = Error.Unauthorized(
      "Corrector.Unauthorized",
      "Missing or invalid authentication token.");

  public static readonly Error Forbidden = Error.Forbidden(
      "Corrector.Forbidden",
      "User does not have permission to perform this operation.");

  public static readonly Error NoSongsLoaded = Error.Problem(
      "Corrector.NoSongsLoaded",
      "No songs were loaded from the database.");

  public static Error FolderNotFound(string folder) => Error.NotFound(
      "Corrector.FolderNotFound",
      $"Folder \"{folder}\" does not exist.");

  public static Error InvalidGuid(string value) => Error.Problem(
      "Corrector.InvalidGuid",
      $"Parameter \"{value}\" is not a valid Guid.");

  public static Error TransactionNotFound(Guid guid) => Error.NotFound(
      "Corrector.TransactionNotFound",
      $"Transaction \"{guid}\" does not exist.");
}
