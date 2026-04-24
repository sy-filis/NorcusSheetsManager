namespace NorcusSheetsManager.Application.Abstractions.Models;

public interface ITransactionResponse
{
  string? Message { get; }
  bool Success { get; }
}
