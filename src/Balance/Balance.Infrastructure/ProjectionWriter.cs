using Balance.Application;
using Balance.Domain;
using Dapper;
using Integration.Contracts;
using Npgsql;
namespace Balance.Infrastructure;

public sealed class ProjectionWriter(NpgsqlDataSource source) : IProjectionWriter
{
 static ProjectionWriter()=>UtcInstantMapping.Configure();
 public async Task ApplyAsync(EntryRecorded message,CancellationToken ct)
 {
    message.Validate();
    await using var connection=await source.OpenConnectionAsync(ct);
    await using var tx=await connection.BeginTransactionAsync(ct);
    CommandDefinition Cmd(string sql,object? args=null)=>new(sql,args,tx,commandTimeout:15,cancellationToken:ct);
    // Every writer takes the account lock before inbox/statement changes, including first delivery.
    await connection.ExecuteAsync(Cmd("SELECT pg_advisory_xact_lock(hashtextextended(@account::text,0))",new{account=message.AccountId}));
    var inserted=await connection.ExecuteAsync(Cmd("INSERT INTO balance.processed_messages(message_id,processed_at) VALUES(@MessageId,clock_timestamp()) ON CONFLICT DO NOTHING",message));
    if(inserted==0){await tx.CommitAsync(ct);return;}
    await connection.ExecuteAsync(Cmd("""
        INSERT INTO balance.account_balances(account_id,customer_id,opened_at,processed_at)
        VALUES(@AccountId,@CustomerId,@OpenedAt,clock_timestamp()) ON CONFLICT DO NOTHING
        """,message));
    await connection.ExecuteAsync(Cmd("""
        INSERT INTO balance.statement_entries(message_id,account_id,sequence,type,amount,registered_at,reversal_of)
        VALUES(@MessageId,@AccountId,@Sequence,@Type,@Amount,@RegisteredAt,@ReversalOf)
        """,message));
    var current=await connection.QuerySingleAsync<Position>(Cmd("SELECT amount AS Amount,last_sequence AS Sequence,customer_id AS Customer FROM balance.account_balances WHERE account_id=@AccountId FOR UPDATE",message));
    if(current.Customer!=message.CustomerId)throw new ArgumentException("Event ownership mismatch.");
    var amount=current.Amount;var sequence=current.Sequence;
    // Persist out-of-order deliveries, but never expose a noncontiguous projection prefix.
    while(true)
    {
        var next=await connection.QuerySingleOrDefaultAsync<Pending>(Cmd("""
            SELECT sequence AS Sequence,type AS Type,amount AS Amount,registered_at AS At
            FROM balance.statement_entries WHERE account_id=@AccountId AND sequence=@next
            """,new{message.AccountId,next=sequence+1}));
        if(next is null)break;
        ProjectionPosition.RequireNext(sequence,next.Sequence);
        amount+=next.Type==1?next.Amount:-next.Amount;
        if(amount<0 || amount>999999999999999.9999m)throw new ArgumentException("Invalid projected balance.");
        sequence=next.Sequence;
        if(sequence%100==0)await connection.ExecuteAsync(Cmd("""
            INSERT INTO balance.balance_snapshots(account_id,sequence,registered_at,amount)
            VALUES(@AccountId,@sequence,@at,@amount) ON CONFLICT DO NOTHING
            """,new{message.AccountId,sequence,at=next.At,amount}));
    }
    await connection.ExecuteAsync(Cmd("UPDATE balance.account_balances SET amount=@amount,last_sequence=@sequence,processed_at=clock_timestamp() WHERE account_id=@AccountId",new{amount,sequence,message.AccountId}));
    await tx.CommitAsync(ct);
 }
 private sealed record Position(decimal Amount,long Sequence,string Customer);
 private sealed record Pending(long Sequence,int Type,decimal Amount,DateTimeOffset At);
}
