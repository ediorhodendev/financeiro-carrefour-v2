namespace Ledger.Domain;

public sealed class Entry
{
    private Entry() { }
    public Entry(Guid accountId, long sequence, EntryType type, decimal amount, string description,
        string? externalReference, DateTimeOffset registeredAt, Guid? reversalOf = null)
    {
        Account.ValidateAmount(amount);
        if (accountId == Guid.Empty || sequence <= 0 || !Enum.IsDefined(type)) throw new ArgumentException("Invalid entry.");
        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 500 || externalReference?.Length > 128)
            throw new ArgumentException("Invalid description or external reference.");
        Id = Guid.NewGuid(); AccountId = accountId; Sequence = sequence; Type = type; Amount = amount;
        Description = description.Trim(); ExternalReference = externalReference; RegisteredAt = registeredAt.ToUniversalTime(); ReversalOf = reversalOf;
    }
    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public long Sequence { get; private set; }
    public EntryType Type { get; private set; }
    public decimal Amount { get; private set; }
    public string Description { get; private set; } = "";
    public string? ExternalReference { get; private set; }
    public DateTimeOffset RegisteredAt { get; private set; }
    public Guid? ReversalOf { get; private set; }
    public Entry Reverse(long sequence, string reason, DateTimeOffset at)
    {
        if (ReversalOf is not null) throw new RuleViolation("A reversal cannot be reversed.");
        return new Entry(AccountId, sequence, Type == EntryType.Credit ? EntryType.Debit : EntryType.Credit,
            Amount, reason, null, at, Id);
    }
}
