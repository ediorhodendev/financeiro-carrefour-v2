using Ledger.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
namespace Ledger.Infrastructure.Persistence;

public sealed class LedgerDbContext(DbContextOptions<LedgerDbContext> options,IDataProtectionProvider protection) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Entry> Entries => Set<Entry>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Account>(a=>{
            a.ToTable("accounts","ledger"); a.HasKey(x=>x.Id); a.Property(x=>x.Balance).HasPrecision(19,4);
            a.Property(x=>x.CustomerId).HasMaxLength(200);
        });
        model.Entity<Entry>(e=>{
            e.ToTable("entries","ledger"); e.HasKey(x=>x.Id); e.Property(x=>x.Amount).HasPrecision(19,4);
            var protector=protection.CreateProtector("ledger.free-text.v1");
            e.Property(x=>x.Description).HasConversion(v=>protector.Protect(v),v=>protector.Unprotect(v));
            e.Property(x=>x.ExternalReference).HasConversion(v=>v==null?null:protector.Protect(v),v=>v==null?null:protector.Unprotect(v));
            e.HasIndex(x=>new{x.AccountId,x.Sequence}).IsUnique(); e.HasIndex(x=>x.ReversalOf).IsUnique();
        });
    }
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken=default)
    {
        ChangeTracker.DetectChanges();
        if(ChangeTracker.Entries<Entry>().Any(e=>e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Ledger entries are immutable.");
        return base.SaveChangesAsync(cancellationToken);
    }
}
