namespace Integration.Contracts;

public sealed record EntryRecorded(int Version, Guid MessageId, Guid AccountId, string CustomerId,
    long Sequence, int Type, decimal Amount, DateTimeOffset RegisteredAt, DateTimeOffset OpenedAt, Guid? ReversalOf)
{
    public void Validate()
    {
        if (Version != 1 || MessageId == Guid.Empty || AccountId == Guid.Empty || string.IsNullOrWhiteSpace(CustomerId)
            || CustomerId.Length > 200 || Sequence <= 0 || Type is not (1 or 2) || Amount <= 0
            || Amount > 999999999999999.9999m || decimal.Round(Amount, 4) != Amount
            || RegisteredAt.Offset != TimeSpan.Zero || RegisteredAt < OpenedAt || ReversalOf == MessageId)
            throw new ArgumentException("Invalid integration event.");
    }
}
