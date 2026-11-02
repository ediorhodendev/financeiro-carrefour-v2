using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ledger.Infrastructure.Persistence;
namespace Ledger.Infrastructure;
public sealed class IdempotencyCleanup(IDbContextFactory<LedgerDbContext> contexts,ILogger<IdempotencyCleanup> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var db=await contexts.CreateDbContextAsync(stoppingToken);
                await db.Database.ExecuteSqlRawAsync("""
                    DELETE FROM ledger.idempotency_keys WHERE (scope,key) IN (
                    SELECT scope,key FROM ledger.idempotency_keys WHERE status='Completed' AND expires_at<clock_timestamp()
                    LIMIT 500 FOR UPDATE SKIP LOCKED)
                    """,stoppingToken);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception e){log.LogWarning("Idempotency cleanup deferred: {ErrorType}",e.GetType().Name);}
            try{await Task.Delay(TimeSpan.FromMinutes(5),stoppingToken);}catch(OperationCanceledException){break;}
        }
    }
}
