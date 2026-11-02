using Messaging.Transport;
using System.Net;
using System.Text;
using System.Text.Json;
using Balance.Application;
using Balance.Infrastructure;
using Dapper;
using Integration.Contracts;
using Ledger.Domain;
using Ledger.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RabbitMQ.Client;
using Shouldly;
using Xunit;
namespace Financial.IntegrationTests;

[Collection("Bank")]
public sealed class LedgerTests(BankFixture fixture)
{
 private static async Task<HttpResponseMessage> SendAsync(HttpClient client,Guid account,string key,string body,string? suffix=null)
 {
    using var request=new HttpRequestMessage(HttpMethod.Post,$"/api/v1/contas/{account}/lancamentos"+suffix){Content=new StringContent(body,Encoding.UTF8,"application/json")};
    request.Headers.Add("Idempotency-Key",key);return await client.SendAsync(request);
 }
 private const string Credit="{\"tipo\":\"Credito\",\"valor\":100,\"descricao\":\"Opening credit\"}";
 private const string Debit="{\"tipo\":\"Debito\",\"valor\":1,\"descricao\":\"Debit\"}";
 [Fact] public async Task ReplayReturnsExactStatusAndBodyAndOneEntry()
 {
    using var client=await fixture.ClientAsync();var account=await fixture.NewAccountAsync(client);var key=Guid.NewGuid().ToString();
    var first=await SendAsync(client,account,key,Credit);var second=await SendAsync(client,account,key,Credit);
    first.StatusCode.ShouldBe(HttpStatusCode.Created);second.StatusCode.ShouldBe(first.StatusCode);
    (await second.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());second.Headers.Contains("Idempotency-Replayed").ShouldBeTrue();
    (await SendAsync(client,account,key,Debit)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    await using var c=new NpgsqlConnection(fixture.Postgres.GetConnectionString());
    (await c.QuerySingleAsync<int>("SELECT count(*) FROM ledger.entries WHERE \"AccountId\"=@account",new{account})).ShouldBe(1);
 }
 [Fact] public async Task FiftyParallelRequestsProduceExactlyOneEntry()
 {
    using var client=await fixture.ClientAsync();var account=await fixture.NewAccountAsync(client);var key=Guid.NewGuid().ToString();
    var responses=await Task.WhenAll(Enumerable.Range(0,50).Select(_=>SendAsync(client,account,key,Credit)));
    responses.ShouldAllBe(r=>r.StatusCode==HttpStatusCode.Created||r.StatusCode==HttpStatusCode.Conflict);
    responses.Count(r=>r.StatusCode==HttpStatusCode.Created).ShouldBeGreaterThan(0);
    await using var c=new NpgsqlConnection(fixture.Postgres.GetConnectionString());
    (await c.QuerySingleAsync<int>("SELECT count(*) FROM ledger.entries WHERE \"AccountId\"=@account",new{account})).ShouldBe(1);
 }
 [Fact] public async Task HundredParallelDebitsCannotOverdrawOrLoseUpdates()
 {
    using var client=await fixture.ClientAsync();var account=await fixture.NewAccountAsync(client);
    (await SendAsync(client,account,Guid.NewGuid().ToString(),Credit.Replace(":100",":50"))).StatusCode.ShouldBe(HttpStatusCode.Created);
    var responses=await Task.WhenAll(Enumerable.Range(0,100).Select(_=>SendAsync(client,account,Guid.NewGuid().ToString(),Debit)));
    responses.ShouldAllBe(r=>r.StatusCode==HttpStatusCode.Created||r.StatusCode==HttpStatusCode.Conflict);
    var accepted=responses.Count(r=>r.StatusCode==HttpStatusCode.Created);accepted.ShouldBe(50);
    await using var c=new NpgsqlConnection(fixture.Postgres.GetConnectionString());
    var balance=await c.QuerySingleAsync<decimal>("SELECT \"Balance\" FROM ledger.accounts WHERE \"Id\"=@account",new{account});
    var sum=await c.QuerySingleAsync<decimal>("SELECT sum(CASE WHEN \"Type\"=1 THEN \"Amount\" ELSE -\"Amount\" END) FROM ledger.entries WHERE \"AccountId\"=@account",new{account});
    balance.ShouldBe(0);balance.ShouldBe(sum);
 }
 [Fact] public async Task ReversalIsFullUniqueAndIdempotent()
 {
    using var client=await fixture.ClientAsync();var account=await fixture.NewAccountAsync(client);
    var original=await SendAsync(client,account,Guid.NewGuid().ToString(),Credit);
    using var doc=JsonDocument.Parse(await original.Content.ReadAsStringAsync());var id=doc.RootElement.GetProperty("id").GetGuid();
    var key=Guid.NewGuid().ToString();var suffix=$"/{id}/estorno";const string body="{\"motivo\":\"Correction\"}";
    var first=await SendAsync(client,account,key,body,suffix);first.StatusCode.ShouldBe(HttpStatusCode.Created);
    var replay=await SendAsync(client,account,key,body,suffix);(await replay.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
    (await SendAsync(client,account,Guid.NewGuid().ToString(),body,suffix)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    using var rev=JsonDocument.Parse(await first.Content.ReadAsStringAsync());var reverseId=rev.RootElement.GetProperty("id").GetGuid();
    (await SendAsync(client,account,Guid.NewGuid().ToString(),body,$"/{reverseId}/estorno")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
 }
 [Fact] public async Task ReversalRequiresFundsAndFailedRequestCanBeRetried()
 {
    using var client=await fixture.ClientAsync();var account=await fixture.NewAccountAsync(client);
    var original=await SendAsync(client,account,Guid.NewGuid().ToString(),Credit);
    using var doc=JsonDocument.Parse(await original.Content.ReadAsStringAsync());var id=doc.RootElement.GetProperty("id").GetGuid();
    await SendAsync(client,account,Guid.NewGuid().ToString(),Debit.Replace(":1,",":100,"));
    var key=Guid.NewGuid().ToString();var suffix=$"/{id}/estorno";const string body="{\"motivo\":\"Correction\"}";
    (await SendAsync(client,account,key,body,suffix)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    await SendAsync(client,account,Guid.NewGuid().ToString(),Credit);
    (await SendAsync(client,account,key,body,suffix)).StatusCode.ShouldBe(HttpStatusCode.Created);
 }
 [Fact] public async Task DatabaseRejectsMutationAndFreeTextIsEncrypted()
 {
    using var client=await fixture.ClientAsync();var account=await fixture.NewAccountAsync(client);await SendAsync(client,account,Guid.NewGuid().ToString(),Credit);
    await using var c=new NpgsqlConnection(fixture.Postgres.GetConnectionString());
    var description=await c.QuerySingleAsync<string>("SELECT \"Description\" FROM ledger.entries WHERE \"AccountId\"=@account",new{account});description.ShouldNotContain("Opening credit");
    await Should.ThrowAsync<PostgresException>(async()=>await c.ExecuteAsync("DELETE FROM ledger.entries WHERE \"AccountId\"=@account",new{account}));
 }
 [Theory][InlineData("{}")][InlineData("{\"tipo\":\"Wrong\",\"valor\":1,\"descricao\":\"X\"}")][InlineData("{\"tipo\":\"Credito\",\"valor\":1.12345,\"descricao\":\"X\"}")]
 public async Task InvalidRequestsReturnProblemDetails(string body)
 {
    using var client=await fixture.ClientAsync();var account=await fixture.NewAccountAsync(client);var response=await SendAsync(client,account,Guid.NewGuid().ToString(),body);
    response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
 }
 [Fact] public async Task MissingKeyAndAuthenticationAreRejected()
 {
    using var client=await fixture.ClientAsync();var account=await fixture.NewAccountAsync(client);
    (await client.PostAsync($"/api/v1/contas/{account}/lancamentos",new StringContent(Credit,Encoding.UTF8,"application/json"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    client.DefaultRequestHeaders.Authorization=null;(await client.PostAsync("/api/v1/contas",null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
 }
 [Fact] public async Task RealBrokerOutboxAndDuplicateConsumerKeepOneFinancialEffect()
 {
    using var client=await fixture.ClientAsync();var account=await fixture.NewAccountAsync(client);await SendAsync(client,account,Guid.NewGuid().ToString(),Credit);
    var factory=BrokerTopology.Factory(fixture.Rabbit.Hostname,"bank","integration-only-password");factory.Port=fixture.Rabbit.GetMappedPublicPort(5672);
    await using var connection=await factory.CreateConnectionAsync();await using var channel=await connection.CreateChannelAsync(new CreateChannelOptions(publisherConfirmationsEnabled:true,publisherConfirmationTrackingEnabled:true));
    await BrokerTopology.ConfigureAsync(channel,default);
    var publisher=ActivatorUtilities.CreateInstance<OutboxPublisher>(fixture.Ledger.Services);
    for(var i=0;i<10;i++)await publisher.PublishBatchAsync(channel,default);
    var source=fixture.Balance.Services.GetRequiredService<NpgsqlDataSource>();var writer=new ProjectionWriter(source);
    var found=false;
    while(await channel.BasicGetAsync(BrokerTopology.Queue,false) is {} delivery)
    {
        var message=JsonSerializer.Deserialize<EntryRecorded>(delivery.Body.Span)!;
        await writer.ApplyAsync(message,default);await writer.ApplyAsync(message,default);
        await channel.BasicAckAsync(delivery.DeliveryTag,false);if(message.AccountId==account)found=true;
    }
    found.ShouldBeTrue();var result=await new BalanceQueries(source,TimeProvider.System).GetAsync(account,"demo-customer",null,default);
    result.ShouldNotBeNull();result.Amount.ShouldBe(100);result.LastSequence.ShouldBe(1);
    using var read=fixture.Balance.CreateClient();read.DefaultRequestHeaders.Authorization=client.DefaultRequestHeaders.Authorization;
    (await read.GetAsync($"/api/v1/contas/{account}/saldo")).StatusCode.ShouldBe(HttpStatusCode.OK);
 }
 [Fact] public async Task OutOfOrderEventsWaitForGapAndSnapshotsMatchHistoricalSum()
 {
    var account=Guid.NewGuid();var now=DateTimeOffset.UtcNow.AddMinutes(-5);var source=fixture.Balance.Services.GetRequiredService<NpgsqlDataSource>();var writer=new ProjectionWriter(source);
    EntryRecorded Event(long seq)=>new(1,Guid.NewGuid(),account,"demo-customer",seq,1,1,now.AddSeconds(seq),now,null);
    await writer.ApplyAsync(Event(2),default);var queries=new BalanceQueries(source,TimeProvider.System);
    (await queries.GetAsync(account,"demo-customer",null,default))!.LastSequence.ShouldBe(0);
    await writer.ApplyAsync(Event(1),default);
    for(var i=3;i<=105;i++)await writer.ApplyAsync(Event(i),default);
    (await queries.GetAsync(account,"demo-customer",now.AddSeconds(102),default))!.Amount.ShouldBe(102);
    var page=await queries.StatementAsync(account,"demo-customer",null,null,2,10,default);
    page!.Items.Count.ShouldBe(10);page.Items[0].Sequence.ShouldBe(11);page.Total.ShouldBe(105);
    (await queries.GetAsync(account,"other-customer",null,default)).ShouldBeNull();
 }
}
