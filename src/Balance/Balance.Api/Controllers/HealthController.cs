using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
namespace Balance.Api.Controllers;
[ApiController,AllowAnonymous,Route("health")]
public sealed class HealthController(NpgsqlDataSource source) : ControllerBase
{
 [HttpGet("live")]public IActionResult Live()=>Ok(new{status="alive"});
 [HttpGet("ready")]public async Task<IActionResult> Ready(CancellationToken ct)
 {
    await using var c=await source.OpenConnectionAsync(ct);await using var command=c.CreateCommand();
    command.CommandText="SELECT 1 FROM balance.account_balances LIMIT 1";await command.ExecuteScalarAsync(ct);return Ok(new{status="ready"});
 }
}
