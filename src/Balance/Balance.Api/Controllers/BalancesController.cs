using Balance.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Balance.Api.Controllers;

[ApiController,Authorize(Policy="balance.read")]
[Route("api/v1")]
public sealed class BalancesController(IBalanceQueries queries) : ControllerBase
{
 [HttpGet("contas/{contaId:guid}/saldo")]
 public async Task<IActionResult> Balance(Guid contaId,[FromQuery(Name="em")]DateTimeOffset? at,CancellationToken ct)
 {
    var result=await queries.GetAsync(contaId,Subject,at,ct);
    return result is null?Problem(statusCode:404,title:"Account is missing or its projection is not available yet."):Ok(result);
 }
 [HttpGet("contas/{contaId:guid}/extrato")]
 public async Task<IActionResult> Statement(Guid contaId,[FromQuery(Name="de")]DateTimeOffset? from,[FromQuery(Name="ate")]DateTimeOffset? to,
    [FromQuery(Name="pagina")]int page=1,[FromQuery(Name="tamanho")]int size=50,CancellationToken ct=default)
 {
    var result=await queries.StatementAsync(contaId,Subject,from,to,page,size,ct);
    return result is null?Problem(statusCode:404,title:"Account is missing or its projection is not available yet."):Ok(result);
 }
 [HttpGet("clientes/me/saldo")]
 public async Task<ActionResult<ConsolidatedBalance>> Consolidated([FromQuery(Name="em")]DateTimeOffset? at,CancellationToken ct)=>Ok(await queries.ConsolidatedAsync(Subject,at,ct));
 private string Subject=>User.FindFirst("sub")!.Value;
}
