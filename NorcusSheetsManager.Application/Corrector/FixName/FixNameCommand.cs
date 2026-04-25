using NorcusSheetsManager.Application.Abstractions.Messaging;

namespace NorcusSheetsManager.Application.Corrector.FixName;

public sealed class FixNameCommand : ICommand
{
  public Guid TransactionGuid { get; init; }
  public string? FileName { get; init; }
  public int? SuggestionIndex { get; init; }
  public bool IsAdmin { get; init; }
  public Guid UserId { get; init; }
}
