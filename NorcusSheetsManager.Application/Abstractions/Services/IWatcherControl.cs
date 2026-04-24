namespace NorcusSheetsManager.Application.Abstractions.Services;

public interface IWatcherControl
{
  void StartWatching(bool verbose = false);
  void StopWatching(bool verbose = false);
}
