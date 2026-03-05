using System.Diagnostics;
using System.Text;
using Confluent.Kafka;

namespace Tests.Clients;

public class KafkaClient(string bootstrapServers, string topic) : IDisposable
{
    private readonly IConsumer<Ignore, OrderCreated> _consumer = new ConsumerBuilder<Ignore, OrderCreated>(
            new ConsumerConfig
            {
                BootstrapServers = bootstrapServers,
                GroupId = $"test-consumer-{Guid.NewGuid()}",
                AutoOffsetReset = AutoOffsetReset.Earliest
            })
        .SetValueDeserializer(Json<OrderCreated>.Create())
        .Build();

    private bool _subscribed;

    public bool ConsumeForCurrentTrace(TimeSpan timeout, out OrderCreated? receivedMessage)
    {
        if (!_subscribed)
        {
            _consumer.Subscribe(topic);
            _subscribed = true;
        }
        
        receivedMessage = null;

        var traceId = Activity.Current?.TraceId.ToString() ?? throw new InvalidOperationException("No active Activity — cannot filter Kafka messages by trace.");

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var result = _consumer.Consume(TimeSpan.FromMilliseconds(200));
            if (result?.Message is null) continue;

            DistributedContextPropagator.Current.ExtractTraceIdAndState(result.Message.Headers, ExtractHeaderValue, out var receivedTraceId, out _);
            if (receivedTraceId is null || !receivedTraceId.Contains(traceId, StringComparison.OrdinalIgnoreCase)) continue;
            
            receivedMessage = result.Message.Value;
            return true;
        }

        return false;
    }

    private static void ExtractHeaderValue(object? carrier, string fieldName, out string? fieldValue, out IEnumerable<string>? fieldValues)
    {
        fieldValue = null;
        fieldValues = null;
        if(carrier is not Headers headers) return;
        if(!headers.TryGetLastBytes(fieldName, out var lastBytes) || lastBytes is not {Length: > 0}) return;
        
        fieldValue = Encoding.UTF8.GetString(lastBytes);
    }

    public void Dispose()
    {
        _consumer.Close();
        _consumer.Dispose();
    }
}
