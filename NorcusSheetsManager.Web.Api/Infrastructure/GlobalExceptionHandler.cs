using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NorcusSheetsManager.Web.Api.Infrastructure;

internal sealed class GlobalExceptionHandler(
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
  public async ValueTask<bool> TryHandleAsync(
      HttpContext httpContext,
      Exception exception,
      CancellationToken cancellationToken)
  {
    logger.LogError(exception, "Unhandled exception while handling {Method} {Path}.",
        httpContext.Request.Method, httpContext.Request.Path);

    IResult result = Results.Problem(
        title: "An unexpected error occurred.",
        detail: environment.IsDevelopment() ? exception.ToString() : null,
        statusCode: StatusCodes.Status500InternalServerError,
        type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");

    await result.ExecuteAsync(httpContext);
    return true;
  }
}
