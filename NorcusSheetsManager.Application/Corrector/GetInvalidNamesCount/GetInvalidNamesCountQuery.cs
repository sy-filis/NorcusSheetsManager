using NorcusSheetsManager.Application.Abstractions.Messaging;

namespace NorcusSheetsManager.Application.Corrector.GetInvalidNamesCount;

public sealed class GetInvalidNamesCountQuery : IQuery<int>
{
  public string? Folder { get; set; }
  public bool IsAdmin { get; set; }
  public Guid UserId { get; set; }
}
