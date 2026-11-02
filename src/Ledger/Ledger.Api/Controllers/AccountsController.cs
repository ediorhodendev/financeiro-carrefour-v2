using Ledger.Domain;
using Ledger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Ledger.Api.Controllers;
[ApiController,Authorize(Policy="ledger.write"),Route("api/v1/contas")]
public sealed class AccountsController(LedgerDbContext db) : ControllerBase
{
 [HttpPost]
 public async Task<IActionResult> Open(CancellationToken ct)
 {
    var account=new Account(Guid.NewGuid(),User.FindFirst("sub")!.Value,DateTimeOffset.UtcNow);
    db.Accounts.Add(account);await db.SaveChangesAsync(ct);
    return StatusCode(201,new{account.Id,account.OpenedAt,currency="BRL"});
 }
}
