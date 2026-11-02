using Messaging.Transport;
using System.Text.Json;
using Balance.Application;
using Integration.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
namespace Balance.Worker;

public sealed class ProjectionConsumer(IProjectionWriter writer,IConfiguration config,ILogger<ProjectionConsumer> log) : BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken ct)
 {
    while(!ct.IsCancellationRequested)
    {
        try
        {
            var factory=BrokerTopology.Factory(config["Messaging:Host"]??"rabbitmq",config["Messaging:User"]??"bank",config["Messaging:Password"]??throw new InvalidOperationException("Broker password required."));
            factory.Port=config.GetValue<int?>("Messaging:Port")??5672;
            await using var connection=await factory.CreateConnectionAsync(ct);
            await using var channel=await connection.CreateChannelAsync(cancellationToken:ct);
            await BrokerTopology.ConfigureAsync(channel,ct);
            while(!ct.IsCancellationRequested)
            {
                var delivery=await channel.BasicGetAsync(BrokerTopology.Queue,false,ct);
                if(delivery is null){await Task.Delay(100,ct);continue;}
                try
                {
                    var message=JsonSerializer.Deserialize<EntryRecorded>(delivery.Body.Span)??throw new ArgumentException("Empty event.");
                    await writer.ApplyAsync(message,ct);
                    await channel.BasicAckAsync(delivery.DeliveryTag,false,ct);
                }
                catch(Exception e)when(e is JsonException or ArgumentException)
                {log.LogWarning("Invalid event routed to dead letter queue.");await channel.BasicRejectAsync(delivery.DeliveryTag,false,ct);}
                catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
                catch(Exception)
                {await channel.BasicNackAsync(delivery.DeliveryTag,false,true,ct);throw;}
            }
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
        catch(Exception e)
        {
            log.LogWarning("Consumer cycle deferred: {ErrorType}",e.GetType().Name);
            try{await Task.Delay(TimeSpan.FromSeconds(5),ct);}catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
        }
    }
 }
}
