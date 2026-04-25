using NorcusSheetsManager.Application.Abstractions.Messaging;
using NorcusSheetsManager.Application.Abstractions.Services;
using NorcusSheetsManager.SharedKernel;

namespace NorcusSheetsManager.Application.Corrector.GetInvalidNamesCount;

internal sealed class GetInvalidNamesCountQueryHandler(INameCorrector corrector, IAccessControl access)
    : IQueryHandler<GetInvalidNamesCountQuery, int>
{
  public Task<Result<int>> Handle(GetInvalidNamesCountQuery query, CancellationToken cancellationToken)
  {
    if (!corrector.HasSongs)
    {
      return Task.FromResult(Result.Failure<int>(CorrectorErrors.NoSongsLoaded));
    }

    string? folder = query.Folder;
    if (!access.CanUserRead(query.IsAdmin, query.UserId, ref folder))
    {
      return Task.FromResult(Result.Failure<int>(CorrectorErrors.Forbidden));
    }

    return Task.FromResult(Result.Success(corrector.GetInvalidCount(folder)));
  }
}
