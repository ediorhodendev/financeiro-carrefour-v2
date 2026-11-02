using Ledger.Domain;
namespace Ledger.Application;

public sealed record RegisterEntry(Guid AccountId, string CustomerId, EntryType Type, decimal Amount, string Description, string? ExternalReference);
public sealed record ReverseEntry(Guid AccountId, string CustomerId, Guid OriginalId, string Reason);
public sealed record EntryResult(Guid Id, Guid AccountId, long Sequence, EntryType Type, decimal Amount, DateTimeOffset RegisteredAt, Guid? ReversalOf);
public interface ILedgerSession
{
    Task<Account> LockAccountAsync(Guid accountId, string customerId, CancellationToken ct);
    Task<Entry?> FindEntryAsync(Guid accountId, Guid entryId, CancellationToken ct);
    Task<bool> HasReversalAsync(Guid originalId, CancellationToken ct);
    Task<DateTimeOffset> RegistrationTimeAsync(CancellationToken ct);
    void Add(Entry entry, Account account);
}
public sealed class ResourceMissing() : Exception("Resource not found.");
public sealed class RegisterEntryHandler(ILedgerSession session)
{
    public async Task<EntryResult> HandleAsync(RegisterEntry command, CancellationToken ct)
    {
        var account = await session.LockAccountAsync(command.AccountId, command.CustomerId, ct);
        account.Apply(command.Type, command.Amount);
        var entry = new Entry(account.Id, account.Sequence, command.Type, command.Amount, command.Description,
            command.ExternalReference, await session.RegistrationTimeAsync(ct));
        session.Add(entry, account);
        return Result(entry);
    }
    public static EntryResult Result(Entry entry) => new(entry.Id, entry.AccountId, entry.Sequence, entry.Type,
        entry.Amount, entry.RegisteredAt, entry.ReversalOf);
}
public sealed class ReverseEntryHandler(ILedgerSession session)
{
    public async Task<EntryResult> HandleAsync(ReverseEntry command, CancellationToken ct)
    {
        var account = await session.LockAccountAsync(command.AccountId, command.CustomerId, ct);
        var original = await session.FindEntryAsync(account.Id, command.OriginalId, ct) ?? throw new ResourceMissing();
        if (await session.HasReversalAsync(original.Id, ct)) throw new RuleViolation("Entry already reversed.");
        var reversal = original.Reverse(checked(account.Sequence + 1), command.Reason, await session.RegistrationTimeAsync(ct));
        account.Apply(reversal.Type, reversal.Amount);
        session.Add(reversal, account);
        return RegisterEntryHandler.Result(reversal);
    }
}
