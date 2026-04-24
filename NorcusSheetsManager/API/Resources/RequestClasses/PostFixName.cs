using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NorcusSheetsManager.API.Resources.RequestClasses;

internal class PostFixName
{
  public Guid TransactionGuid { get; set; } = Guid.Empty;
  public string? FileName { get; set; }
  public int? SuggestionIndex { get; set; }
}
