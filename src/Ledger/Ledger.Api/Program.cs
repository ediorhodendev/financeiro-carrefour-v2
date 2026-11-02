using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ledger.Api.Controllers;
using Ledger.Api.Filters;
using Ledger.Application;
using Ledger.Domain;
using Ledger.Infrastructure;
using Ledger.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ServiceDefaults;

var builder=WebApplication.CreateBuilder(args);
var settings=Environment.GetEnvironmentVariable("DEMO_SETTINGS_PATH");
if(settings is not null)
{
    if(args.Contains("--migrate")&&!File.Exists(settings))
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
        await File.WriteAllTextAsync(settings,JsonSerializer.Serialize(new{Jwt=new{SigningKey=Convert.ToHexString(RandomNumberGenerator.GetBytes(64))}}));
    }
    builder.Configuration.AddJsonFile(settings,optional:false);
}
builder.AddBankApi("Ledger.Api");
builder.Services.AddControllers().AddJsonOptions(o=>{o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter<EntryKind>(allowIntegerValues:false));o.JsonSerializerOptions.RespectRequiredConstructorParameters=true;});
var connection=builder.Configuration.GetConnectionString("Bank")??throw new InvalidOperationException("ConnectionStrings__Bank is required.");
builder.Services.AddSingleton(NpgsqlDataSource.Create(connection));
builder.Services.AddDbContextFactory<LedgerDbContext>(o=>o.UseNpgsql(connection));
builder.Services.AddDataProtection().SetApplicationName("DigitalBank.Ledger").PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["Protection:KeyPath"]??Path.Combine(Path.GetTempPath(),"digital-bank-keys")));
builder.Services.AddScoped<ILedgerSession,LedgerSession>();builder.Services.AddScoped<RegisterEntryHandler>();builder.Services.AddScoped<ReverseEntryHandler>();builder.Services.AddScoped<IdempotencyFilter>();
if(builder.Configuration.GetValue<bool>("Messaging:Enabled"))builder.Services.AddHostedService<OutboxPublisher>();
builder.Services.AddHostedService<IdempotencyCleanup>();
var app=builder.Build();
if(args.Contains("--migrate"))
{
    await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
    await db.Database.MigrateAsync();
    if(app.Environment.IsDevelopment()&&builder.Configuration.GetValue<bool>("Demo:Enabled")&&!await db.Accounts.AnyAsync(a=>a.Id==DemoController.AccountId))
    {db.Accounts.Add(new Account(DemoController.AccountId,"demo-customer",DateTimeOffset.UtcNow));await db.SaveChangesAsync();}
    return;
}
app.UseBankApi();app.Run();
public partial class Program { }
