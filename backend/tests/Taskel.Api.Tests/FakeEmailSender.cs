using System.Collections.Concurrent;
using Taskel.Api.Email;

namespace Taskel.Api.Tests;

/// <summary>Records replies instead of sending them. Failures are set up per recipient, so parallel tests do not interfere.</summary>
public class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<ReplyEmail> _sent = new();
    private readonly ConcurrentDictionary<string, Func<Exception>> _failures = new();

    public IReadOnlyList<ReplyEmail> SentTo(string address) => _sent.Where(m => m.To == address).ToList();

    public void FailFor(string address, Func<Exception> failure) => _failures[address] = failure;

    public void StopFailingFor(string address) => _failures.TryRemove(address, out _);

    public Task SendReplyAsync(ReplyEmail reply, CancellationToken ct = default)
    {
        if (_failures.TryGetValue(reply.To, out var failure)) throw failure();
        _sent.Enqueue(reply);
        return Task.CompletedTask;
    }
}
