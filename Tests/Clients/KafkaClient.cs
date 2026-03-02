using System.Diagnostics;
using System.Text;
using Confluent.Kafka;

namespace Tests.Clients;

public class KafkaClient(string bootstrapServers, string topic) : IDisposable
{
    private readonly IConsumer<Ignore, string> _consumer = new ConsumerBuilder<Ignore, string>(
            new ConsumerConfig
            {
                BootstrapServers = bootstrapServers,
                GroupId = $"test-consumer-{Guid.NewGuid()}",
                AutoOffsetReset = AutoOffsetReset.Earliest
            })
        .SetValueDeserializer(Deserializers.Utf8)
        .Build();

    private bool _subscribed;

    public List<string> ConsumeForCurrentTrace(TimeSpan timeout)
    {
        if (!_subscribed)
        {
            _consumer.Subscribe(topic);
            _subscribed = true;
        }

        var traceId = Activity.Current?.TraceId.ToString()
            ?? throw new InvalidOperationException("No active Activity — cannot filter Kafka messages by trace.");

        var messages = new List<string>();
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var result = _consumer.Consume(TimeSpan.FromMilliseconds(200));
            if (result?.Message is null) continue;

            var traceparent = GetHeaderValue(result.Message.Headers, "traceparent");
            if (traceparent is not null && traceparent.Contains(traceId, StringComparison.OrdinalIgnoreCase))
            {
                messages.Add(result.Message.Value);
            }
        }

        return messages;
    }

    private static string? GetHeaderValue(Headers? headers, string key)
    {
        if (headers is null) return null;

        try
        {
            var header = headers.GetLastBytes(key);
            return header is not null ? Encoding.UTF8.GetString(header) : null;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _consumer.Close();
        _consumer.Dispose();
    }
}
