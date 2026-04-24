using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NorcusSheetsManager;

internal static class Logger
{
  public static void Debug(string message, NLog.Logger nLogger)
  {
    nLogger.Debug(message);
    Console.WriteLine(DateTime.Now.ToLongTimeString() + ": " + message);
  }
  public static void Warn(string message, NLog.Logger nLogger)
  {
    nLogger.Warn(message);
    Console.WriteLine(DateTime.Now.ToLongTimeString() + ": " + "Warning: " + message);
  }
  public static void Warn(Exception message, NLog.Logger nLogger)
  {
    nLogger.Warn(message);
    Console.WriteLine(DateTime.Now.ToLongTimeString() + ": " + "Warning: " + message);
  }
  public static void Error(string message, NLog.Logger nLogger)
  {
    nLogger.Error(message);
    Console.WriteLine(DateTime.Now.ToLongTimeString() + ": " + "Error: " + message);
  }
  public static void Error(Exception message, NLog.Logger nLogger)
  {
    nLogger.Error(message);
    Console.WriteLine(DateTime.Now.ToLongTimeString() + ": " + "Error: " + message);
  }
}
