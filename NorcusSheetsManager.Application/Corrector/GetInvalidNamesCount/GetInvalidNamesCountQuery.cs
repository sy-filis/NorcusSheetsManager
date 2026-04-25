using NorcusSheetsManager.Application.Abstractions.Messaging;

namespace NorcusSheetsManager.Application.Corrector.GetInvalidNamesCount;

public sealed class GetInvalidNamesCountQuery : IQuery<int>
{
  public string? Folder { get; init; }
  public bool IsAdmin { get; init; }
  public Guid UserId { get; init; }
}
