using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NorcusSheetsManager.NameCorrector;

internal class TransactionResponse(bool success, string? message = null) : ITransactionResponse
{
  public bool Success { get; } = success;
  public string? Message { get; } = message;
}
