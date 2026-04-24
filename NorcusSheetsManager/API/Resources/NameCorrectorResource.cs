using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NorcusSheetsManager.API.Resources.RequestClasses;
using NorcusSheetsManager.NameCorrector;

namespace NorcusSheetsManager.API.Resources;

internal static class NameCorrectorResource
{
  public static void MapEndpoints(IEndpointRouteBuilder app)
  {
    var group = app.MapGroup("/api/v1/corrector");

    group.MapGet("/invalid-names/{suggestionsCount:int?}",
        (int? suggestionsCount, ITokenAuthenticator auth, Corrector corrector, HttpContext ctx)
            => GetInvalidNames(null, suggestionsCount, auth, corrector, ctx));
    group.MapGet("/{folder}/invalid-names/{suggestionsCount:int?}",
        (string folder, int? suggestionsCount, ITokenAuthenticator auth, Corrector corrector, HttpContext ctx)
            => GetInvalidNames(folder, suggestionsCount, auth, corrector, ctx));

    group.MapGet("/count",
        (ITokenAuthenticator auth, Corrector corrector, HttpContext ctx)
            => GetCount(null, auth, corrector, ctx));
    group.MapGet("/{folder}/count",
        (string folder, ITokenAuthenticator auth, Corrector corrector, HttpContext ctx)
            => GetCount(folder, auth, corrector, ctx));

    group.MapPost("/fix-name", FixName);

    group.MapDelete("/{transaction}", DeleteFile);

    group.MapGet("/file-exists/{transaction}/{fileName}", CheckFileExists);
  }

  private static async Task<IResult> GetInvalidNames(string? folder, int? suggestionsCount,
      ITokenAuthenticator auth, Corrector corrector, HttpContext ctx)
  {
    if (!auth.ValidateFromContext(ctx))
    {
      return Results.Unauthorized();
    }

    if (!corrector.ReloadData())
    {
      return Results.Text("No songs were loaded from the database.", statusCode: StatusCodes.Status500InternalServerError);
    }

    bool isAdmin = auth.ValidateFromContext(ctx, new Claim("NsmAdmin", "true"));
    var userId = new Guid(auth.GetClaimValue(ctx, "uuid") ?? Guid.Empty.ToString());
    var model = new Models.NameCorrectorModel(corrector.DbLoader);
    if (!model.CanUserRead(isAdmin, userId, ref folder))
    {
      return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    int count = suggestionsCount ?? 1;

    IEnumerable<IRenamingTransaction>? transactions;
    Type serializationType;
    if (string.IsNullOrEmpty(folder))
    {
      transactions = corrector.GetRenamingTransactionsForAllSubfolders(count);
      serializationType = typeof(IEnumerable<IRenamingTransaction>);
    }
    else
    {
      transactions = corrector.GetRenamingTransactions(folder, count);
      serializationType = typeof(IEnumerable<IRenamingTransactionBase>);
    }

    if (transactions is null)
    {
      return Results.Text($"Bad request: Folder \"{folder ?? corrector.BaseSheetsFolder}\" does not exist.",
          statusCode: StatusCodes.Status400BadRequest);
    }

    string json = JsonSerializer.Serialize(transactions, serializationType);
    await Task.CompletedTask;
    return Results.Content(json, "application/json; charset=utf-8");
  }

  private static IResult GetCount(string? folder, ITokenAuthenticator auth, Corrector corrector, HttpContext ctx)
  {
    if (!auth.ValidateFromContext(ctx))
    {
      return Results.Unauthorized();
    }

    if (!corrector.ReloadData())
    {
      return Results.Text("No songs were loaded from the database.", statusCode: StatusCodes.Status500InternalServerError);
    }

    bool isAdmin = auth.ValidateFromContext(ctx, new Claim("NsmAdmin", "true"));
    var userId = new Guid(auth.GetClaimValue(ctx, "uuid") ?? Guid.Empty.ToString());
    var model = new Models.NameCorrectorModel(corrector.DbLoader);
    if (!model.CanUserRead(isAdmin, userId, ref folder))
    {
      return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    IEnumerable<IRenamingTransaction>? transactions = string.IsNullOrEmpty(folder)
        ? corrector.GetRenamingTransactionsForAllSubfolders(1)
        : corrector.GetRenamingTransactions(folder, 1);

    if (transactions is null)
    {
      return Results.Text($"Bad request: Folder \"{folder ?? corrector.BaseSheetsFolder}\" does not exist.",
          statusCode: StatusCodes.Status400BadRequest);
    }

    return Results.Text(transactions.Count().ToString());
  }

  private static async Task<IResult> FixName(ITokenAuthenticator auth, Corrector corrector, HttpContext ctx)
  {
    if (!auth.ValidateFromContext(ctx))
    {
      return Results.Unauthorized();
    }

    bool isAdmin = auth.ValidateFromContext(ctx, new Claim("NsmAdmin", "true"));
    var userId = new Guid(auth.GetClaimValue(ctx, "uuid") ?? Guid.Empty.ToString());
    var model = new Models.NameCorrectorModel(corrector.DbLoader);
    if (!model.CanUserCommit(isAdmin, userId))
    {
      return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    PostFixName? request;
    try
    {
      request = await JsonSerializer.DeserializeAsync<PostFixName>(ctx.Request.Body);
    }
    catch
    {
      return Results.BadRequest();
    }

    if (request is null)
    {
      return Results.BadRequest();
    }

    if (string.IsNullOrEmpty(request.FileName) && !request.SuggestionIndex.HasValue)
    {
      var msg = new StringBuilder("Bad request: Both \"FileName\" and \"SuggestionIndex\" values are null. One of them must be set.");
      if (request.TransactionGuid == Guid.Empty)
      {
        msg.Append(" Parameter \"TransactionGuid\" is invalid");
      }

      return Results.Text(msg.ToString(), statusCode: StatusCodes.Status400BadRequest);
    }

    var response = request.SuggestionIndex.HasValue
        ? corrector.CommitTransactionByGuid(request.TransactionGuid, (int)request.SuggestionIndex)
        : corrector.CommitTransactionByGuid(request.TransactionGuid, request.FileName!);

    if (!response.Success)
    {
      return Results.Text(response.Message ?? "", statusCode: StatusCodes.Status500InternalServerError);
    }

    return Results.Ok();
  }

  private static IResult DeleteFile(string transaction, ITokenAuthenticator auth, Corrector corrector, HttpContext ctx)
  {
    if (!auth.ValidateFromContext(ctx))
    {
      return Results.Unauthorized();
    }

    bool isAdmin = auth.ValidateFromContext(ctx, new Claim("NsmAdmin", "true"));
    var userId = new Guid(auth.GetClaimValue(ctx, "uuid") ?? Guid.Empty.ToString());
    var model = new Models.NameCorrectorModel(corrector.DbLoader);
    if (!model.CanUserCommit(isAdmin, userId))
    {
      return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    if (!Guid.TryParse(transaction, out Guid guid))
    {
      return Results.Text($"Bad request: Parameter \"{transaction}\" is not valid Guid.",
          statusCode: StatusCodes.Status400BadRequest);
    }

    var response = corrector.DeleteTransaction(guid);
    if (!response.Success)
    {
      return Results.Text(response.Message ?? "", statusCode: StatusCodes.Status500InternalServerError);
    }

    return Results.Ok();
  }

  private static IResult CheckFileExists(string transaction, string fileName,
      ITokenAuthenticator auth, Corrector corrector, HttpContext ctx)
  {
    if (!auth.ValidateFromContext(ctx))
    {
      return Results.Unauthorized();
    }

    var errorMsg = new StringBuilder();
    if (!Guid.TryParse(transaction, out Guid guid))
    {
      errorMsg.AppendLine($"Parameter \"{transaction}\" is not valid Guid.");
    }

    var trans = corrector.GetTransactionByGuid(guid);
    if (trans is null)
    {
      errorMsg.AppendLine($"Transaction \"{guid}\" does not exist.");
    }

    if (errorMsg.Length > 0)
    {
      return Results.Text($"Bad request: {errorMsg}", statusCode: StatusCodes.Status400BadRequest);
    }

    IRenamingSuggestion suggestion = new Suggestion(trans!.InvalidFullPath, fileName, 0);
    return Results.Json(suggestion);
  }
}
