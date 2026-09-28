using System.Collections.Concurrent;

namespace HiWallet.Sms.Fake;

public sealed record SmsMessage(Guid MessageId, string To, string Text, DateTimeOffset SentAt);

/// <summary>Gönderilmiş sayılan mesajlar, bellekte. Sınırlı: en eski mesaj düşüyor.</summary>
public sealed class SmsInbox
{
    private const int Capacity = 1_000;

    private readonly ConcurrentQueue<SmsMessage> _messages = new();

    public void Add(SmsMessage message)
    {
        _messages.Enqueue(message);

        while (_messages.Count > Capacity && _messages.TryDequeue(out _))
        {
        }
    }

    /// <summary>Yeniden eskiye.</summary>
    public IReadOnlyList<SmsMessage> For(string? to, int size) =>
        _messages
            .Where(m => to is null || m.To == to)
            .Reverse()
            .Take(size)
            .ToList();
}
