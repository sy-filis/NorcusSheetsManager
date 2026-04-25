using NorcusSheetsManager.Application.Abstractions.Messaging;
using NorcusSheetsManager.Application.Abstractions.Models;

namespace NorcusSheetsManager.Application.Corrector.CheckFileExists;

public sealed class CheckFileExistsQuery : IQuery<IRenamingSuggestion>
{
  public Guid TransactionGuid { get; init; }
  public string FileName { get; init; } = "";
}
