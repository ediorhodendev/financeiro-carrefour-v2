using System.Data;
using Balance.Application;
using Dapper;
using Npgsql;
namespace Balance.Infrastructure;

public sealed class BalanceQueries(NpgsqlDataSource source,TimeProvider clock) : IBalanceQueries
{
 public async Task<BalanceResult?> GetAsync(Guid account,string customer,DateTimeOffset? at,CancellationToken ct)
 {
    ValidateTime(at);
    await using var c=await source.OpenConnectionAsync(ct);
    await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
    var result=await ReadAsync(c,tx,account,customer,at,ct);await tx.CommitAsync(ct);return result;
 }
 private static async Task<BalanceResult?> ReadAsync(NpgsqlConnection c,NpgsqlTransaction tx,Guid account,string customer,DateTimeOffset? at,CancellationToken ct)
 {
    var position=await c.QuerySingleOrDefaultAsync<Row>(new("""
        SELECT account_id AS Id,amount AS Amount,last_sequence AS Sequence,processed_at AS ProcessedAt,opened_at AS OpenedAt
        FROM balance.account_balances WHERE account_id=@account AND customer_id=@customer
        """,new{account,customer},tx,cancellationToken:ct));
    if(position is null)return null;
    if(at<position.OpenedAt)throw new ArgumentException("Instant predates account opening.");
    var amount=position.Amount;
    if(at is not null)
    {
        amount=await c.QuerySingleAsync<decimal>(new("""
            WITH snapshot AS (
                SELECT sequence,amount FROM balance.balance_snapshots WHERE account_id=@account
                AND registered_at<=@at AND sequence<=@sequence ORDER BY sequence DESC LIMIT 1)
            SELECT COALESCE((SELECT amount FROM snapshot),0) + COALESCE((
                SELECT SUM(CASE WHEN type=1 THEN amount ELSE -amount END) FROM balance.statement_entries
                WHERE account_id=@account AND sequence>COALESCE((SELECT sequence FROM snapshot),0)
                AND sequence<=@sequence AND registered_at<=@at),0)
            """,new{account,at,sequence=position.Sequence},tx,cancellationToken:ct));
    }
    return new(position.Id,"BRL",amount,at,position.Sequence,position.ProcessedAt);
 }
 public async Task<StatementPage?> StatementAsync(Guid account,string customer,DateTimeOffset? from,DateTimeOffset? to,int page,int size,CancellationToken ct)
 {
    ValidateTime(from);ValidateTime(to);
    if(from>to||page<1||size is <1 or >100||page>1000000)throw new ArgumentException("Invalid statement range or pagination.");
    await using var c=await source.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
    var position=await ReadAsync(c,tx,account,customer,null,ct);if(position is null)return null;
    const string filter=" FROM balance.statement_entries WHERE account_id=@account AND sequence<=@sequence AND (@from::timestamptz IS NULL OR registered_at>=@from) AND (@to::timestamptz IS NULL OR registered_at<=@to)";
    var args=new{account,sequence=position.LastSequence,from,to,size,offset=(page-1)*size};
    var total=await c.QuerySingleAsync<long>(new("SELECT COUNT(*)"+filter,args,tx,cancellationToken:ct));
    var items=(await c.QueryAsync<StatementItem>(new("SELECT message_id AS Id,sequence AS Sequence,type AS Type,amount AS Amount,registered_at AS RegisteredAt,reversal_of AS ReversalOf"+filter+" ORDER BY sequence LIMIT @size OFFSET @offset",args,tx,cancellationToken:ct))).ToArray();
    await tx.CommitAsync(ct);return new(items,page,size,total,position.LastSequence);
 }
 public async Task<ConsolidatedBalance> ConsolidatedAsync(string customer,DateTimeOffset? at,CancellationToken ct)
 {
    ValidateTime(at);await using var c=await source.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
    var ids=await c.QueryAsync<Guid>(new("SELECT account_id FROM balance.account_balances WHERE customer_id=@customer AND (@at::timestamptz IS NULL OR opened_at<=@at) ORDER BY account_id",new{customer,at},tx,cancellationToken:ct));
    var accounts=new List<BalanceResult>();foreach(var id in ids){var item=await ReadAsync(c,tx,id,customer,at,ct);if(item is not null)accounts.Add(item);}
    await tx.CommitAsync(ct);return new(customer,"BRL",accounts.Sum(x=>x.Amount),at,accounts);
 }
 private void ValidateTime(DateTimeOffset? at)
 {
    if(at is not null&&(at.Value.Offset!=TimeSpan.Zero||at>clock.GetUtcNow()))throw new ArgumentException("Use a UTC instant that is not in the future.");
 }
 private sealed record Row(Guid Id,decimal Amount,long Sequence,DateTimeOffset ProcessedAt,DateTimeOffset OpenedAt);
}
