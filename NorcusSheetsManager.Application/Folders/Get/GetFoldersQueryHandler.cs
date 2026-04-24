using NorcusSheetsManager.Application.Abstractions.Messaging;
using NorcusSheetsManager.Application.Abstractions.Services;
using NorcusSheetsManager.SharedKernel;

namespace NorcusSheetsManager.Application.Folders.Get;

internal sealed class GetFoldersQueryHandler(IFolderBrowser browser) : IQueryHandler<GetFoldersQuery, IReadOnlyList<string>>
{
  public Task<Result<IReadOnlyList<string>>> Handle(GetFoldersQuery query, CancellationToken cancellationToken)
  {
    IReadOnlyList<string> folders = browser.GetSheetFolders().ToList();
    return Task.FromResult(Result.Success(folders));
  }
}
