using NorcusSheetsManager.Application.Abstractions.Messaging;

namespace NorcusSheetsManager.Application.Folders.Get;

public sealed record GetFoldersQuery : IQuery<IReadOnlyList<string>>;
