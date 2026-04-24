namespace NorcusSheetsManager.Application.Abstractions.Services;

public interface IAccessControl
{
  /// <summary>
  /// If the user is an admin, always returns true. For non-admins: the user must exist;
  /// if they request another user's folder, returns false; if they request all folders, returns true
  /// and <paramref name="sheetsFolder"/> is reassigned to that user's folder.
  /// </summary>
  bool CanUserRead(bool isAdmin, Guid userId, ref string? sheetsFolder);

  /// <summary>
  /// Admins can do anything. For others we only check that the user exists.
  /// </summary>
  bool CanUserCommit(bool isAdmin, Guid userId);
}
