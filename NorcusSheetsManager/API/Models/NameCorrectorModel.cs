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
  /// Pokud je uživatel admin, vrací vždy true. Pro neadminy platí: Uživatel musí existovat;
  /// pokud žádá o cizí složku, vrací se false; pokud žádá o všechny složky, vrací se true
  /// a do <paramref name="sheetsFolder"/> se nastaví složka uživatele.
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

    // User existuje a není admin:
    if (string.IsNullOrEmpty(sheetsFolder)) // Chce získat info ke všem složkám.
                                            // Vrátím true, ale přes referenci mu vrátím jen jeho složku.
    {
      sheetsFolder = user.Folder;
      return true;
    }

    if (sheetsFolder != user.Folder) // Chce získat info k cizí složce
    {
      return false;
    }

    return true;
  }
  /// <summary>
  /// Pokud je admin, tak může všechno. Jinak testuji, jestli uživatel alespoň existuje.
  /// Složku, kde chce dělat úpravu kotrolovat nemusím, protože aby mohl zapisovat,
  /// musí znát Guid transakce, který by při čtení nezískal.
  /// Odkud může číst, tam může i zapisovat. Pokud by mohl číst i cizí složky, musela by se kontrolovat i složka.
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
