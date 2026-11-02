using System.Text.Json;
using Integration.Contracts;
using Ledger.Application;
using Ledger.Domain;
using Microsoft.EntityFrameworkCore;
namespace Ledger.Infrastructure.Persistence;

public sealed class LedgerSession(LedgerDbContext db) : ILedgerSession
{
    public async Task<Account> LockAccountAsync(Guid id,string customer,CancellationToken ct)
    {
        if(db.Database.CurrentTransaction is null) throw new InvalidOperationException("A transaction boundary is required.");
        var rows=await db.Accounts.FromSqlInterpolated($"SELECT * FROM ledger.accounts WHERE \"Id\"={id} AND \"CustomerId\"={customer} FOR UPDATE").ToListAsync(ct);
        return rows.SingleOrDefault() ?? throw new ResourceMissing();
    }
    public Task<Entry?> FindEntryAsync(Guid account,Guid entry,CancellationToken ct)=>db.Entries.AsNoTracking().SingleOrDefaultAsync(e=>e.AccountId==account&&e.Id==entry,ct);
    public Task<bool> HasReversalAsync(Guid original,CancellationToken ct)=>db.Entries.AnyAsync(e=>e.ReversalOf==original,ct);
    public async Task<DateTimeOffset> RegistrationTimeAsync(CancellationToken ct)
    {
        var result=await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").ToListAsync(ct);return result.Single();
    }
    public void Add(Entry entry,Account account)
    {
        db.Entries.Add(entry);
        var message=new EntryRecorded(1,entry.Id,account.Id,account.CustomerId,entry.Sequence,(int)entry.Type,entry.Amount,entry.RegisteredAt,account.OpenedAt,entry.ReversalOf);
        db.Database.ExecuteSqlInterpolated($"INSERT INTO ledger.outbox_messages(id,payload,created_at) VALUES ({entry.Id},{JsonSerializer.Serialize(message)}::jsonb,{entry.RegisteredAt})");
    }
}
