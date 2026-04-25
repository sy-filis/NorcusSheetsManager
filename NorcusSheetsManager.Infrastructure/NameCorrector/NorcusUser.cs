using NorcusSheetsManager.Application.Abstractions.Models;

namespace NorcusSheetsManager.Infrastructure.NameCorrector;

public class NorcusUser : INorcusUser
{
  public Guid Guid { get; init; } = Guid.Empty;
  public string Folder { get; init; } = "";
  public string Email { get; init; } = "";
  public string Name { get; init; } = "";
}
