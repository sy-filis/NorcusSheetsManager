using NorcusSheetsManager.Application.Abstractions.Messaging;

namespace NorcusSheetsManager.Application.Corrector.FixName;

public sealed class FixNameCommand : ICommand
{
  public Guid TransactionGuid { get; set; }
  public string? FileName { get; set; }
  public int? SuggestionIndex { get; set; }
  public bool IsAdmin { get; set; }
  public Guid UserId { get; set; }
}
