using NorcusSheetsManager.Application.Abstractions.Messaging;

namespace NorcusSheetsManager.Application.Corrector.AutoFixInvalidNames;

public sealed class AutoFixInvalidNamesCommand : ICommand<AutoFixInvalidNamesResponse>
{
  public bool IsAdmin { get; init; }
  public Guid UserId { get; init; }
}
