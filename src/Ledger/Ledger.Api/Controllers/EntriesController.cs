using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Ledger.Api.Filters;
using Ledger.Application;
using Ledger.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Ledger.Api.Controllers;

public enum EntryKind { Credito=1, Debito=2 }
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EntryRequest(
    [property:JsonPropertyName("tipo")][Required] EntryKind Type,
    [property:JsonPropertyName("valor")] decimal Amount,
    [property:JsonPropertyName("descricao")][Required,StringLength(500,MinimumLength=1)] string Description,
    [property:JsonPropertyName("referenciaExterna")][StringLength(128)] string? ExternalReference=null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReversalRequest([property:JsonPropertyName("motivo")][Required,StringLength(500,MinimumLength=1)] string Reason);

[ApiController]
[Route("api/v1/contas/{contaId:guid}/lancamentos")]
[Authorize(Policy="ledger.write")]
public sealed class EntriesController(RegisterEntryHandler register,ReverseEntryHandler reverse) : ControllerBase
{
    [HttpPost,Idempotent]
    [ProducesResponseType<EntryResult>(201)]
    public async Task<IActionResult> Register(Guid contaId,EntryRequest request,CancellationToken ct)
    {
        if(!Enum.IsDefined(request.Type))return Problem(statusCode:400,title:"Invalid entry type.");
        var result=await register.HandleAsync(new(contaId,User.FindFirst("sub")!.Value,(EntryType)request.Type,request.Amount,request.Description,request.ExternalReference),ct);
        return StatusCode(201,result);
    }
    [HttpPost("{lancamentoId:guid}/estorno"),Idempotent]
    [ProducesResponseType<EntryResult>(201)]
    public async Task<IActionResult> Reverse(Guid contaId,Guid lancamentoId,ReversalRequest request,CancellationToken ct)
    {
        var result=await reverse.HandleAsync(new(contaId,User.FindFirst("sub")!.Value,lancamentoId,request.Reason),ct);
        return StatusCode(201,result);
    }
}
