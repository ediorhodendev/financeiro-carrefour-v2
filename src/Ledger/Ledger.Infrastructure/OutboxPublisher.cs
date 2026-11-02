using Messaging.Transport;
using System.Text;
using Dapper;
using Integration.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using RabbitMQ.Client;
namespace Ledger.Infrastructure;

public sealed class OutboxPublisher(NpgsqlDataSource source,IConfiguration config,ILogger<OutboxPublisher> log) : BackgroundService
{
 private readonly ResiliencePipeline pipeline=new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions{MaxRetryAttempts=2,Delay=TimeSpan.FromMilliseconds(200),BackoffType=DelayBackoffType.Exponential,UseJitter=true})
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions{FailureRatio=.5,MinimumThroughput=4,SamplingDuration=TimeSpan.FromSeconds(30),BreakDuration=TimeSpan.FromSeconds(10)})
    .AddTimeout(TimeSpan.FromSeconds(10)).Build();
 protected override async Task ExecuteAsync(CancellationToken ct)
 {
    while(!ct.IsCancellationRequested)
    {
        try
        {
            var factory=BrokerTopology.Factory(config["Messaging:Host"]??"rabbitmq",config["Messaging:User"]??"bank",config["Messaging:Password"]??throw new InvalidOperationException("Broker password required."));
            factory.Port=config.GetValue<int?>("Messaging:Port")??5672;
            await using var connection=await factory.CreateConnectionAsync(ct);
            await using var channel=await connection.CreateChannelAsync(new CreateChannelOptions(publisherConfirmationsEnabled:true,publisherConfirmationTrackingEnabled:true),ct);
            await BrokerTopology.ConfigureAsync(channel,ct);
            while(!ct.IsCancellationRequested){await PublishBatchAsync(channel,ct);await Task.Delay(250,ct);}
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
        catch(Exception e){log.LogWarning("Outbox cycle deferred: {ErrorType}",e.GetType().Name);await DelayAsync(ct);}
    }
 }
 public async Task PublishBatchAsync(IChannel channel,CancellationToken ct)
 {
    await using var c=await source.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
    var messages=await c.QueryAsync<Pending>(new("""
        SELECT id AS Id,payload::text AS Payload,attempts AS Attempts FROM ledger.outbox_messages
        WHERE published_at IS NULL AND dead_lettered_at IS NULL AND next_attempt_at<=clock_timestamp()
        ORDER BY created_at,id LIMIT 20 FOR UPDATE SKIP LOCKED
        """,transaction:tx,cancellationToken:ct));
    foreach(var message in messages)
    {
        try
        {
            await pipeline.ExecuteAsync(async token=>await channel.BasicPublishAsync(BrokerTopology.Exchange,"",true,
                new BasicProperties{Persistent=true,ContentType="application/json",MessageId=message.Id.ToString()},Encoding.UTF8.GetBytes(message.Payload),token),ct);
            await c.ExecuteAsync(new("UPDATE ledger.outbox_messages SET published_at=clock_timestamp() WHERE id=@Id",message,tx,cancellationToken:ct));
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
        catch(Exception e)
        {
            // Quarantine keeps the event, never silently discards it. Replay requires operator diagnosis.
            await c.ExecuteAsync(new("""
                UPDATE ledger.outbox_messages SET attempts=attempts+1,
                next_attempt_at=clock_timestamp()+make_interval(secs=>LEAST(300,power(2,LEAST(attempts,8)))+random()),
                dead_lettered_at=CASE WHEN attempts+1>=20 THEN clock_timestamp() ELSE NULL END WHERE id=@Id
                """,message,tx,cancellationToken:ct));
            log.LogWarning("Outbox publication deferred: {ErrorType}",e.GetType().Name);
        }
    }
    await tx.CommitAsync(ct);
 }
 private static async Task DelayAsync(CancellationToken ct){try{await Task.Delay(TimeSpan.FromSeconds(5),ct);}catch(OperationCanceledException)when(ct.IsCancellationRequested){}}
 private sealed record Pending(Guid Id,string Payload,int Attempts);
}
