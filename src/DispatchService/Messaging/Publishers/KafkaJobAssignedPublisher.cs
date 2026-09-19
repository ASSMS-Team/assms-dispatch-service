using Confluent.Kafka;
using DispatchService.Models;

namespace DispatchService.Messaging.Publishers;

/// <summary>Publishes persisted outbox payloads and completes only after Kafka acknowledges them.</summary>
public sealed class KafkaJobAssignedPublisher : IJobAssignedPublisher
{
    private readonly IProducer<string, string> _producer;

    public KafkaJobAssignedPublisher(string bootstrapServers)
    {
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
        }).Build();
    }

    public async Task PublishAsync(PendingJobAssignedEvent pendingEvent, CancellationToken cancellationToken)
    {
        await _producer.ProduceAsync(
            pendingEvent.Topic,
            new Message<string, string> { Key = pendingEvent.JobId, Value = pendingEvent.Payload },
            cancellationToken);
    }

    public void Dispose() => _producer.Dispose();
}
