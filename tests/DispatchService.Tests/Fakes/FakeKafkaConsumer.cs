using Confluent.Kafka;

namespace DispatchService.Tests.Fakes;

public class FakeKafkaConsumer : IConsumer<string, string>
{
    private readonly Queue<ConsumeResult<string, string>> _messages = new();
    public List<string> SubscribedTopics { get; } = [];
    public List<TopicPartitionOffset> CommittedOffsets { get; } = [];
    public List<TopicPartitionOffset> SeekedOffsets { get; } = [];
    public int CloseCallCount { get; private set; }
    public Action? OnMessagesExhausted { get; set; }
    public Message<string, string>? LastConsumedMessage { get; private set; }

    public ConsumeResult<string, string> Enqueue(string value)
    {
        var result = new ConsumeResult<string, string>
        {
            Topic = "job-created", Partition = new Partition(0), Offset = new Offset(_messages.Count),
            Message = new Message<string, string> { Key = "test-job", Value = value },
        };
        _messages.Enqueue(result);
        return result;
    }

    public ConsumeResult<string, string> Consume(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_messages.Count == 0)
        {
            OnMessagesExhausted?.Invoke();
            throw new OperationCanceledException();
        }
        var message = _messages.Dequeue();
        LastConsumedMessage = message.Message;
        return message;
    }

    public void Subscribe(string topic) => SubscribedTopics.Add(topic);
    public void Subscribe(IEnumerable<string> topics) => SubscribedTopics.AddRange(topics);
    public void Commit(ConsumeResult<string, string> result) => CommittedOffsets.Add(result.TopicPartitionOffset);
    public void Seek(TopicPartitionOffset offset) => SeekedOffsets.Add(offset);
    public void Close() => CloseCallCount++;
    public void Dispose() { }

    public Handle Handle => throw new NotSupportedException();
    public string Name => nameof(FakeKafkaConsumer);
    public string MemberId => nameof(FakeKafkaConsumer);
    public List<TopicPartition> Assignment => [];
    public List<string> Subscription => SubscribedTopics;
    public IConsumerGroupMetadata ConsumerGroupMetadata => throw new NotSupportedException();
    public int AddBrokers(string brokers) => throw new NotSupportedException();
    public void SetSaslCredentials(string username, string password) => throw new NotSupportedException();
    public ConsumeResult<string, string> Consume(int millisecondsTimeout) => throw new NotSupportedException();
    public ConsumeResult<string, string> Consume(TimeSpan timeout) => throw new NotSupportedException();
    public void Unsubscribe() => throw new NotSupportedException();
    public void Assign(TopicPartition partition) => throw new NotSupportedException();
    public void Assign(TopicPartitionOffset partition) => throw new NotSupportedException();
    public void Assign(IEnumerable<TopicPartitionOffset> partitions) => throw new NotSupportedException();
    public void Assign(IEnumerable<TopicPartition> partitions) => throw new NotSupportedException();
    public void IncrementalAssign(IEnumerable<TopicPartition> partitions) => throw new NotSupportedException();
    public void IncrementalAssign(IEnumerable<TopicPartitionOffset> partitions) => throw new NotSupportedException();
    public void IncrementalUnassign(IEnumerable<TopicPartition> partitions) => throw new NotSupportedException();
    public void Unassign() => throw new NotSupportedException();
    public void StoreOffset(ConsumeResult<string, string> result) => throw new NotSupportedException();
    public void StoreOffset(TopicPartitionOffset offset) => throw new NotSupportedException();
    public List<TopicPartitionOffset> Commit() => throw new NotSupportedException();
    public void Commit(IEnumerable<TopicPartitionOffset> offsets) => throw new NotSupportedException();
    public void Pause(IEnumerable<TopicPartition> partitions) => throw new NotSupportedException();
    public void Resume(IEnumerable<TopicPartition> partitions) => throw new NotSupportedException();
    public List<TopicPartitionOffset> Committed(TimeSpan timeout) => throw new NotSupportedException();
    public List<TopicPartitionOffset> Committed(IEnumerable<TopicPartition> partitions, TimeSpan timeout) => throw new NotSupportedException();
    public Offset Position(TopicPartition partition) => throw new NotSupportedException();
    public List<TopicPartitionOffset> OffsetsForTimes(IEnumerable<TopicPartitionTimestamp> timestampsToSearch, TimeSpan timeout) => throw new NotSupportedException();
    public WatermarkOffsets GetWatermarkOffsets(TopicPartition topicPartition) => throw new NotSupportedException();
    public WatermarkOffsets QueryWatermarkOffsets(TopicPartition topicPartition, TimeSpan timeout) => throw new NotSupportedException();
}
