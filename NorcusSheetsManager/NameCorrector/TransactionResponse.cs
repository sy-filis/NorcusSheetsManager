using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NorcusSheetsManager.NameCorrector;

internal class TransactionResponse : ITransactionResponse
{
  public bool Success { get; }
  public string? Message { get; }
  public TransactionResponse(bool success, string? message = null)
  {
    Success = success;
    Message = message;
  }
}
