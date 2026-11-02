using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
namespace Ledger.Api.Controllers;
[ApiController,AllowAnonymous,Route("demo/access")]
public sealed class DemoController(IConfiguration config,IHostEnvironment environment) : ControllerBase
{
 public static readonly Guid AccountId=Guid.Parse("c90a3c2c-cdeb-4df0-85cb-d1a299a0d101");
 [HttpPost]
 public IActionResult Access()
 {
    if(!environment.IsDevelopment()||!config.GetValue<bool>("Demo:Enabled"))return NotFound();
    var key=config["Jwt:SigningKey"]??throw new InvalidOperationException("Demo signing key missing.");
    var token=new JwtSecurityToken("digital-bank-demo","digital-bank",[new Claim("sub","demo-customer"),new Claim("scope","ledger.write balance.read")],
        DateTime.UtcNow.AddSeconds(-5),DateTime.UtcNow.AddHours(1),new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),SecurityAlgorithms.HmacSha256));
    return Ok(new{accountId=AccountId,token=new JwtSecurityTokenHandler().WriteToken(token),expiresInSeconds=3600});
 }
}
