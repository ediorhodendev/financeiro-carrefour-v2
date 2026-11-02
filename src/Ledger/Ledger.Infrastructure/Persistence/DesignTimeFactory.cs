using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace Ledger.Infrastructure.Persistence;
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<LedgerDbContext>
{
 public LedgerDbContext CreateDbContext(string[] args)=>new(new DbContextOptionsBuilder<LedgerDbContext>().UseNpgsql("Host=localhost;Database=bank_design;Username=design;Password=design-only").Options,new EphemeralDataProtectionProvider());
}
