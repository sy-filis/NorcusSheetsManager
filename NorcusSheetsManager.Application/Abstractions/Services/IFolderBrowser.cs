namespace NorcusSheetsManager.Application.Abstractions.Services;

public interface IFolderBrowser
{
  IEnumerable<string> GetSheetFolders();
}
