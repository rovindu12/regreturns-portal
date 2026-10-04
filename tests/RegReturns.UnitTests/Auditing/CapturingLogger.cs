using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

namespace RegReturns.UnitTests.Auditing;

/// <summary>Keeps every log entry so a test can assert on level, event id and rendered message.</summary>
/// <typeparam name="T">The category type.</typeparam>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Entries => [.. _entries];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        _entries.Enqueue(new LogEntry(logLevel, eventId.Id, formatter(state, exception), exception));
    }
}

/// <summary>One captured log entry.</summary>
internal sealed record LogEntry(LogLevel Level, int EventId, string Message, Exception? Exception);
