using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NorcusSheetsManager.NameCorrector;

namespace NorcusSheetsManager.API.Models;

internal class NameCorrectorModel(IDbLoader dbLoader)
{
  /// <summary>
  /// If the user is an admin, always returns true. For non-admins: the user must exist;
  /// if they request another user's folder, returns false; if they request all folders, returns true
  /// and <paramref name="sheetsFolder"/> is reassigned to that user's folder.
  /// </summary>
  /// <param name="nsmAdmin"></param>
  /// <param name="guid"></param>
  /// <param name="sheetsFolder"></param>
  /// <returns></returns>
  public bool CanUserRead(bool nsmAdmin, Guid guid, ref string? sheetsFolder)
  {
    if (nsmAdmin)
    {
      return true;
    }

    INorcusUser? user = dbLoader.GetUsers().FirstOrDefault(u => u.Guid == guid);
    if (user is null)
    {
      return false;
    }

    // User exists and is not an admin:
    if (string.IsNullOrEmpty(sheetsFolder)) // Requesting info for all folders.
                                            // Return true, but hand back only their folder via the ref parameter.
    {
      sheetsFolder = user.Folder;
      return true;
    }

    if (sheetsFolder != user.Folder) // Requesting info for another user's folder
    {
      return false;
    }

    return true;
  }
  /// <summary>
  /// Admins can do anything. For others we only check that the user exists.
  /// We don't need to check which folder they are writing to, because to write they must
  /// know the transaction Guid, which they could only obtain from a read they were allowed.
  /// Wherever they can read, they can also write. If they could read foreign folders too, we'd need a folder check here as well.
  /// </summary>
  /// <param name="nsmAdmin"></param>
  /// <param name="guid"></param>
  /// <returns></returns>
  public bool CanUserCommit(bool nsmAdmin, Guid guid)
  {
    if (nsmAdmin)
    {
      return true;
    }

    INorcusUser? user = dbLoader.GetUsers().FirstOrDefault(u => u.Guid == guid);

    return user is not null;
  }
}
