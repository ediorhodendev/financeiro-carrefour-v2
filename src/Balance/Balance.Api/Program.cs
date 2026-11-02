using Balance.Application;
using Balance.Infrastructure;
using Npgsql;
using ServiceDefaults;
var builder=WebApplication.CreateBuilder(args);
var settings=Environment.GetEnvironmentVariable("DEMO_SETTINGS_PATH");if(settings is not null)builder.Configuration.AddJsonFile(settings,optional:false);
builder.AddBankApi("Balance.Api");
builder.Services.AddSingleton(NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Bank")??throw new InvalidOperationException("Bank connection required.")));
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);builder.Services.AddScoped<IBalanceQueries,BalanceQueries>();
var app=builder.Build();app.UseBankApi();app.Run();
public partial class Program { }
