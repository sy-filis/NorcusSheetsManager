using NorcusSheetsManager.SharedKernel;

namespace NorcusSheetsManager.Application.Abstractions.Messaging;

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
  Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken);
}
