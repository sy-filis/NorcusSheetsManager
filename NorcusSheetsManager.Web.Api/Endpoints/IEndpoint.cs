using Microsoft.AspNetCore.Routing;

namespace NorcusSheetsManager.Web.Api.Endpoints;

public interface IEndpoint
{
  void MapEndpoint(IEndpointRouteBuilder app);
}
