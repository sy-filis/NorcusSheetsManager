using NorcusSheetsManager.Application.Abstractions.Models;

namespace NorcusSheetsManager.Infrastructure.NameCorrector;

public class NorcusUser : INorcusUser
{
  public Guid Guid { get; set; } = Guid.Empty;
  public string Folder { get; set; } = "";
  public string Email { get; set; } = "";
  public string Name { get; set; } = "";
}
