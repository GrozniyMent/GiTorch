using System.Threading.Channels;
using gitorch.Models;
using gitorch.Services.Abstractions;

namespace gitorch.Services;

public sealed class InMemoryJobQueue : IJobQueue
{
    private readonly Channel<JobRequest> _channel = Channel.CreateUnbounded<JobRequest>();

    public ValueTask<bool> EnqueueAsync(JobRequest job, CancellationToken ct = default) =>
        ValueTask.FromResult(_channel.Writer.TryWrite(job));

    public IAsyncEnumerable<JobRequest> DequeueAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);

    public void Complete() => _channel.Writer.TryComplete();
}

