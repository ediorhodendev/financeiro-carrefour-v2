namespace Ledger.Domain;

public sealed class RuleViolation(string message) : Exception(message);
public enum EntryType { Credit = 1, Debit = 2 }

public sealed class Account
{
    public const decimal Maximum = 999999999999999.9999m;
    private Account() { }
    public Account(Guid id, string customerId, DateTimeOffset openedAt)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(customerId) || customerId.Length > 200)
            throw new ArgumentException("Invalid account identity.");
        Id = id; CustomerId = customerId; OpenedAt = openedAt.ToUniversalTime();
    }
    public Guid Id { get; private set; }
    public string CustomerId { get; private set; } = "";
    public DateTimeOffset OpenedAt { get; private set; }
    public decimal Balance { get; private set; }
    public long Sequence { get; private set; }
    public void Apply(EntryType type, decimal amount)
    {
        ValidateAmount(amount);
        if (!Enum.IsDefined(type)) throw new ArgumentException("Unknown entry type.");
        if (type == EntryType.Debit && amount > Balance) throw new RuleViolation("Insufficient funds.");
        if (type == EntryType.Credit && amount > Maximum - Balance) throw new RuleViolation("Balance limit exceeded.");
        Balance += type == EntryType.Credit ? amount : -amount;
        Sequence = checked(Sequence + 1);
    }
    public static void ValidateAmount(decimal value)
    {
        if (value <= 0 || value > Maximum || decimal.Round(value, 4) != value)
            throw new ArgumentException("Amount must be positive, fit NUMERIC(19,4) and have at most four fractional digits.");
    }
}
