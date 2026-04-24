namespace NorcusSheetsManager.Application.Corrector.AutoFixInvalidNames;

public sealed record AutoFixInvalidNamesResponse(
    int TotalCount,
    int FixedCount,
    IReadOnlyList<string> Failures);
