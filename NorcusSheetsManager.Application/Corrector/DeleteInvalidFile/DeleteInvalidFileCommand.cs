using NorcusSheetsManager.Application.Abstractions.Messaging;

namespace NorcusSheetsManager.Application.Corrector.DeleteInvalidFile;

public sealed class DeleteInvalidFileCommand : ICommand
{
  public Guid TransactionGuid { get; init; }
  public bool IsAdmin { get; init; }
  public Guid UserId { get; init; }
}
