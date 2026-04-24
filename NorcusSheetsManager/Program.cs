using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using NorcusSheetsManager;
using NorcusSheetsManager.NameCorrector;

namespace AutoPdfToImage;

internal class Program
{
  public static readonly string VERSION = _GetVersion();
  private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
  private static void Main(string[] args)
  {
    try
    {
      Console.WriteLine("Norcus Sheets Manager " + VERSION);
      Console.WriteLine("-------------------------");
      var manager = new Manager();
      manager.FullScan();
      manager.StartWatching(true);
      if (manager.Config.Converter.AutoScan)
      {
        manager.AutoFullScan(60000, 5);
      }

      string commandMessage = """
                  Commands:
                      S -- Scan all PDF files (checks whether all PDFs have any image)
                      D -- Deep scan (checks image files count vs PDF page count)
                      F -- Force convert (converts all PDF files)
                      C|N -- Correct invalid file Names
                      X|T -- sTop program
              """;

      Console.WriteLine(commandMessage);

      bool @continue = true;
      while (@continue)
      {
        switch (Console.ReadKey(true).Key.ToString())
        {
          case "S":
            manager.FullScan();
            break;
          case "D":
            manager.DeepScan();
            break;
          case "F":
            Console.WriteLine("Are you sure? (Y/N)");
            if (Console.ReadKey(true).Key.ToString() == "Y")
            {
              manager.ForceConvertAll();
            }

            break;
          case "C":
          case "N":
            CorrectNames(manager);
            break;
          case "T":
          case "X":
            @continue = false;
            break;
          default:
            break;
        }
        Console.WriteLine(commandMessage);
      }
      ;
    }
    catch (Exception e)
    {
      Logger.Error(e, _logger);
      Console.ReadLine();
    }
  }
  private static string _GetVersion()
  {
    string version = Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString() ?? "";
    while (version.EndsWith('0') || version.EndsWith("."))
    {
      version = version.Substring(0, version.Length - 1);
    }
    return version;
  }
  private static void CorrectNames(Manager manager)
  {
    Console.WriteLine("--------------------");
    Console.WriteLine("File name corrector:");
    Console.WriteLine("--------------------");
    manager.NameCorrector.ReloadData();
    IEnumerable<IRenamingTransaction>? transactions = manager.NameCorrector.GetRenamingTransactionsForAllSubfolders(1);

    if (transactions is null || !transactions.Any())
    {
      Console.WriteLine("No incorrectly named files were found.");
      Console.WriteLine("--------------------------------------");
      return;
    }

    Console.WriteLine("Invalid file names and suggestions:");
    foreach (IRenamingTransaction trans in transactions)
    {
      IRenamingSuggestion? suggestion = trans.Suggestions.FirstOrDefault();
      Console.WriteLine($"{trans.InvalidFullPath} -> " +
          (suggestion is null ? "<NO SUGGESTION>" : $"{Path.GetFileNameWithoutExtension(suggestion?.FullPath)}") +
          ((suggestion?.FileExists ?? false) ? " (FILE EXISTS!)" : ""));
    }
    if (transactions.Count() == 0)
    {
      return;
    }

    Console.WriteLine("Correct all file names? (Y/N)");
    if (Console.ReadKey(true).Key.ToString().Equals("Y"))
    {
      manager.StopWatching();
      foreach (IRenamingTransaction trans in transactions)
      {
        ITransactionResponse response = trans.Commit(0);
        if (!response.Success)
        {
          Console.WriteLine(response.Message);
        }
      }
      manager.StartWatching();
      Console.WriteLine("File names correction finished.");
    }
    else
    {
      Console.WriteLine("File names correction aborted.");
    }
    Console.WriteLine("-----------------------------------------");
  }
}
