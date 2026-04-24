using NorcusSheetsManager.Application.Abstractions.Messaging;
using NorcusSheetsManager.Application.Abstractions.Models;

namespace NorcusSheetsManager.Application.Corrector.CheckFileExists;

public sealed class CheckFileExistsQuery : IQuery<IRenamingSuggestion>
{
  public Guid TransactionGuid { get; set; }
  public string FileName { get; set; } = "";
}
