using RabbitMQ.Client;
namespace Messaging.Transport;
public static class BrokerTopology
{
 public const string Exchange="bank.entries.v1",Queue="bank.balance.v1",DeadQueue="bank.balance.dead.v1";
 public static async Task ConfigureAsync(IChannel channel,CancellationToken ct)
 {
    await channel.ExchangeDeclareAsync(Exchange,ExchangeType.Fanout,true,cancellationToken:ct);
    await channel.ExchangeDeclareAsync("bank.dead.v1",ExchangeType.Fanout,true,cancellationToken:ct);
    await channel.QueueDeclareAsync(DeadQueue,true,false,false,new Dictionary<string,object?>{["x-queue-type"]="quorum"},cancellationToken:ct);
    await channel.QueueBindAsync(DeadQueue,"bank.dead.v1","",cancellationToken:ct);
    await channel.QueueDeclareAsync(Queue,true,false,false,new Dictionary<string,object?>{
        ["x-queue-type"]="quorum",["x-delivery-limit"]=10,["x-dead-letter-exchange"]="bank.dead.v1",
        ["x-dead-letter-strategy"]="at-least-once",["x-overflow"]="reject-publish"},cancellationToken:ct);
    await channel.QueueBindAsync(Queue,Exchange,"",cancellationToken:ct);
 }
 public static ConnectionFactory Factory(string host,string user,string password)=>new(){HostName=host,UserName=user,Password=password,RequestedConnectionTimeout=TimeSpan.FromSeconds(5),AutomaticRecoveryEnabled=true};
}
