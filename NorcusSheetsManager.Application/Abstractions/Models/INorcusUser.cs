namespace NorcusSheetsManager.Application.Abstractions.Models;

public interface INorcusUser
{
  Guid Guid { get; }
  string Folder { get; }
  string Email { get; }
  string Name { get; }
}
