using NorcusSheetsManager.Application.Abstractions.Models;
using NorcusSheetsManager.Application.Abstractions.Services;
using NorcusSheetsManager.Infrastructure.NameCorrector;

namespace NorcusSheetsManager.Infrastructure.Services;

internal sealed class AccessControl(IDbLoader dbLoader) : IAccessControl
{
  public bool CanUserRead(bool isAdmin, Guid userId, ref string? sheetsFolder)
  {
    if (isAdmin)
    {
      return true;
    }

    INorcusUser? user = dbLoader.GetUsers().FirstOrDefault(u => u.Guid == userId);
    if (user is null)
    {
      return false;
    }

    // User exists and is not an admin:
    if (string.IsNullOrEmpty(sheetsFolder))
    {
      sheetsFolder = user.Folder;
      return true;
    }

    return sheetsFolder == user.Folder;
  }

  public bool CanUserCommit(bool isAdmin, Guid userId)
  {
    if (isAdmin)
    {
      return true;
    }

    INorcusUser? user = dbLoader.GetUsers().FirstOrDefault(u => u.Guid == userId);
    return user is not null;
  }
}
