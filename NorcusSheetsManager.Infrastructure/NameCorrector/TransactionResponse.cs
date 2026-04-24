using NorcusSheetsManager.Application.Abstractions.Models;

namespace NorcusSheetsManager.Infrastructure.NameCorrector;

internal class TransactionResponse(bool success, string? message = null) : ITransactionResponse
{
  public bool Success { get; } = success;
  public string? Message { get; } = message;
}
