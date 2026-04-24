using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NorcusSheetsManager.NameCorrector;

public interface INorcusUser
{
  Guid Guid { get; }
  string Folder { get; }
  string Email { get; }
  string Name { get; }
}
public class NorcusUser : INorcusUser
{
  public Guid Guid { get; set; } = Guid.Empty;
  public string Folder { get; set; } = "";
  public string Email { get; set; } = "";
  public string Name { get; set; } = "";
}
