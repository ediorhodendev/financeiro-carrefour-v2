using Integration.Contracts;
namespace Balance.Application;
public sealed record BalanceResult(Guid AccountId,string Currency,decimal Amount,DateTimeOffset? At,long LastSequence,DateTimeOffset ProcessedAt);
public sealed record StatementItem(Guid Id,long Sequence,int Type,decimal Amount,DateTimeOffset RegisteredAt,Guid? ReversalOf);
public sealed record StatementPage(IReadOnlyList<StatementItem> Items,int Page,int Size,long Total,long LastSequence);
public sealed record ConsolidatedBalance(string CustomerId,string Currency,decimal Amount,DateTimeOffset? At,IReadOnlyList<BalanceResult> Accounts);
public interface IProjectionWriter { Task ApplyAsync(EntryRecorded message,CancellationToken ct); }
public interface IBalanceQueries
{
 Task<BalanceResult?> GetAsync(Guid account,string customer,DateTimeOffset? at,CancellationToken ct);
 Task<StatementPage?> StatementAsync(Guid account,string customer,DateTimeOffset? from,DateTimeOffset? to,int page,int size,CancellationToken ct);
 Task<ConsolidatedBalance> ConsolidatedAsync(string customer,DateTimeOffset? at,CancellationToken ct);
}
