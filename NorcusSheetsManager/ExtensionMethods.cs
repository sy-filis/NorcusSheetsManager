using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NorcusSheetsManager;

internal static class ExtensionMethods
{
  public static string ToStringList<T>(this IEnumerable<T> list, string separator)
  {
    var sb = new StringBuilder();
    foreach (T item in list)
    {
      sb.Append(item?.ToString() ?? "");
      sb.Append(separator);
    }
    sb.Remove(sb.Length - separator.Length, separator.Length);
    return sb.ToString();
  }
}
