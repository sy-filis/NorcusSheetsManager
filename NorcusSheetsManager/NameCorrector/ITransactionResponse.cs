namespace NorcusSheetsManager.NameCorrector;

internal interface ITransactionResponse
{
  string? Message { get; }
  bool Success { get; }
}
