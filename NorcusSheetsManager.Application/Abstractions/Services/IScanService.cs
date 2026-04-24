namespace NorcusSheetsManager.Application.Abstractions.Services;

public interface IScanService
{
  void FullScan();
  void DeepScan();
  void ForceConvertAll();
}
