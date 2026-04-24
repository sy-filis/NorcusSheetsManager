using NorcusSheetsManager.Application.Abstractions.Models;

namespace NorcusSheetsManager.Application.Corrector.GetInvalidNames;

public sealed record InvalidNamesResponse(IEnumerable<IRenamingTransaction> Transactions, bool IsFolderScoped);
