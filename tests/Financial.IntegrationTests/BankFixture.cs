extern alias LedgerHost;
extern alias BalanceHost;
using System.Security.Cryptography;
using Dapper;
using Ledger.Domain;
using Ledger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;
namespace Financial.IntegrationTests;

public sealed class BankFixture : IAsyncLifetime
{
 public PostgreSqlContainer Postgres {get;}=new PostgreSqlBuilder("postgres:18.6").Build();
 public RabbitMqContainer Rabbit {get;}=new RabbitMqBuilder("rabbitmq:4.4.0-management").WithUsername("bank").WithPassword("integration-only-password").Build();
 public WebApplicationFactory<LedgerHost::Program> Ledger {get;private set;}=null!;
 public WebApplicationFactory<BalanceHost::Program> Balance {get;private set;}=null!;
 public async Task InitializeAsync()
 {
    await Task.WhenAll(Postgres.StartAsync(),Rabbit.StartAsync());
    var settings=new Dictionary<string,string?>{
        ["ConnectionStrings:Bank"]=Postgres.GetConnectionString(),["Jwt:SigningKey"]=Convert.ToHexString(RandomNumberGenerator.GetBytes(64)),
        ["Demo:Enabled"]="true",["Messaging:Enabled"]="false",["Protection:KeyPath"]=Path.Combine(Path.GetTempPath(),"bank-tests-"+Guid.NewGuid()),
        ["Messaging:Host"]=Rabbit.Hostname,["Messaging:Port"]=Rabbit.GetMappedPublicPort(5672).ToString(),["Messaging:User"]="bank",["Messaging:Password"]="integration-only-password"};
    Ledger=new WebApplicationFactory<LedgerHost::Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(settings)));
    using(var scope=Ledger.Services.CreateScope())
    {
        var db=scope.ServiceProvider.GetRequiredService<LedgerDbContext>();await db.Database.MigrateAsync();
        db.Accounts.Add(new Account(Guid.Parse("c90a3c2c-cdeb-4df0-85cb-d1a299a0d101"),"demo-customer",DateTimeOffset.UtcNow));await db.SaveChangesAsync();
    }
    Balance=new WebApplicationFactory<BalanceHost::Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(settings)));
 }
 public async Task DisposeAsync(){await Ledger.DisposeAsync();await Balance.DisposeAsync();await Rabbit.DisposeAsync();await Postgres.DisposeAsync();}
 public async Task<HttpClient> ClientAsync()
 {
    var client=Ledger.CreateClient();var response=await client.PostAsync("/demo/access",null);
    using var doc=System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    client.DefaultRequestHeaders.Authorization=new("Bearer",doc.RootElement.GetProperty("token").GetString());return client;
 }
 public async Task<Guid> NewAccountAsync(HttpClient client)
 {
    var response=await client.PostAsync("/api/v1/contas",null);response.EnsureSuccessStatusCode();
    using var doc=System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());return doc.RootElement.GetProperty("id").GetGuid();
 }
}
[CollectionDefinition("Bank")]
public sealed class BankCollection : ICollectionFixture<BankFixture> { }
