using NorcusSheetsManager.Application.Abstractions.Messaging;

namespace NorcusSheetsManager.Application.Corrector.GetInvalidNames;

public sealed class GetInvalidNamesQuery : IQuery<InvalidNamesResponse>
{
  public string? Folder { get; set; }
  public int? SuggestionsCount { get; set; }
  public bool IsAdmin { get; set; }
  public Guid UserId { get; set; }
}
