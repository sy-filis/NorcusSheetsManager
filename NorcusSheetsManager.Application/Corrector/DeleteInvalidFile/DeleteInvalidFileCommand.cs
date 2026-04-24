using NorcusSheetsManager.Application.Abstractions.Messaging;

namespace NorcusSheetsManager.Application.Corrector.DeleteInvalidFile;

public sealed class DeleteInvalidFileCommand : ICommand
{
  public Guid TransactionGuid { get; set; }
  public bool IsAdmin { get; set; }
  public Guid UserId { get; set; }
}
