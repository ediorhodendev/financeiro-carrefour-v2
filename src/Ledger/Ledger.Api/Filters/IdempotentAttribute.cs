using System.Text;
using System.Text.Json;
using Dapper;
using Ledger.Application;
using Ledger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;
namespace Ledger.Api.Filters;

[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute() : TypeFilterAttribute(typeof(IdempotencyFilter));

public sealed class IdempotencyFilter(LedgerDbContext db,IOptions<JsonOptions> json) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context,ActionExecutionDelegate next)
    {
        var http=context.HttpContext;var ct=http.RequestAborted;
        if(!http.Request.Headers.TryGetValue("Idempotency-Key",out var keys)||keys.Count!=1)
        { context.Result=Problem(400,"Provide one Idempotency-Key header.");return; }
        try { IdempotencyHash.ValidateKey(keys[0]); }
        catch(ArgumentException e){context.Result=Problem(400,e.Message);return;}
        var scope=(Guid)context.ActionArguments["contaId"]!;
        var subject=http.User.FindFirst("sub")?.Value;
        if(!await db.Accounts.AsNoTracking().AnyAsync(a=>a.Id==scope&&a.CustomerId==subject,ct))
        {context.Result=Problem(404,"Account not found.");return;}
        http.Request.Body.Position=0;
        using var reader=new StreamReader(http.Request.Body,Encoding.UTF8,leaveOpen:true);
        var body=await reader.ReadToEndAsync(ct);http.Request.Body.Position=0;
        var hash=IdempotencyHash.Compute(http.Request.Method+" "+http.Request.Path,body);
        await using var transaction=await db.Database.BeginTransactionAsync(ct);
        var connection=db.Database.GetDbConnection();var tx=transaction.GetDbTransaction();
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='750ms'; SET LOCAL statement_timeout='15s';",ct);
            var reserved=await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO ledger.idempotency_keys(scope,key,request_hash,status,expires_at)
                VALUES (@scope,@key,@hash,'InProgress',clock_timestamp()+interval '48 hours')
                ON CONFLICT(scope,key) DO NOTHING
                """,new{scope,key=keys[0],hash},tx,cancellationToken:ct));
            if(reserved==0)
            {
                var row=await connection.QuerySingleAsync<StoredResponse>(new CommandDefinition("""
                    SELECT request_hash AS Hash,status AS Status,status_code AS Code,response_body AS Body
                    FROM ledger.idempotency_keys WHERE scope=@scope AND key=@key
                    """,new{scope,key=keys[0]},tx,cancellationToken:ct));
                if(row.Hash!=hash){context.Result=Problem(422,"Idempotency key already used with different content.");return;}
                if(row.Status!="Completed"){http.Response.Headers.RetryAfter="1";context.Result=Problem(409,"Request is in progress.");return;}
                http.Response.Headers["Idempotency-Replayed"]="true";
                context.Result=new ContentResult{StatusCode=row.Code,Content=row.Body,ContentType="application/json"};return;
            }
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='5s';",ct);
            var executed=await next();
            if(executed.Exception is not null&&!executed.ExceptionHandled)return;
            if(executed.Result is not ObjectResult result || (result.StatusCode??200)>=400)return;
            var response=JsonSerializer.Serialize(result.Value,result.Value?.GetType()??typeof(object),json.Value.JsonSerializerOptions);
            await db.SaveChangesAsync(ct);
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE ledger.idempotency_keys SET status='Completed',status_code=@code,response_body=@response
                WHERE scope=@scope AND key=@key
                """,new{code=result.StatusCode??200,response,scope,key=keys[0]},tx,cancellationToken:ct));
            await transaction.CommitAsync(ct);
            executed.Result=new ContentResult{StatusCode=result.StatusCode??200,Content=response,ContentType="application/json"};
        }
        catch(PostgresException e) when(e.SqlState is PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.UniqueViolation)
        {http.Response.Headers.RetryAfter="1";context.Result=Problem(409,"Concurrent request in progress; retry the same key.");}
    }
    private static ObjectResult Problem(int status,string title)=>new(new ProblemDetails{Status=status,Title=title}){StatusCode=status};
    private sealed record StoredResponse(string Hash,string Status,int Code,string Body);
}
