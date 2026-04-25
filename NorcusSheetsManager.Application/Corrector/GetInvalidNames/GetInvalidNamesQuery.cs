using NorcusSheetsManager.Application.Abstractions.Messaging;

namespace NorcusSheetsManager.Application.Corrector.GetInvalidNames;

public sealed class GetInvalidNamesQuery : IQuery<InvalidNamesResponse>
{
  public string? Folder { get; init; }
  public int? SuggestionsCount { get; init; }
  public bool IsAdmin { get; init; }
  public Guid UserId { get; init; }
}
