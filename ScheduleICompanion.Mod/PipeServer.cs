using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using MelonLoader;
using ScheduleICompanion.Shared;

namespace ScheduleICompanion.Mod;

public sealed class PipeServer : IDisposable
{
    public const string PipeName = "ScheduleICompanion.v1";
    private readonly MelonLogger.Instance _logger;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<string> _backlog = new();
    private readonly object _writerLock = new();
    private StreamWriter? _writer;
    private Task? _worker;

    public event Action<BridgeMessage>? MessageReceived;

    public PipeServer(MelonLogger.Instance logger) => _logger = logger;

    public void Start() => _worker = Task.Run(() => AcceptLoopAsync(_stop.Token));

    public void Publish(BridgeMessage message)
    {
        var json = JsonSerializer.Serialize(message);

        lock (_writerLock)
        {
            if (_writer is not null)
            {
                try
                {
                    _writer.WriteLine(json);
                    _writer.Flush();
                    return;
                }
                catch
                {
                    _writer = null;
                }
            }
        }

        _backlog.Enqueue(json);
        while (_backlog.Count > 250 && _backlog.TryDequeue(out _)) { }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                _logger.Msg("Waiting for Schedule I Companion...");
                await pipe.WaitForConnectionAsync(cancellationToken);
                _logger.Msg("Schedule I Companion connected.");

                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
                {
                    AutoFlush = true
                };

                lock (_writerLock)
                    _writer = writer;

                while (_backlog.TryDequeue(out var pending))
                    await writer.WriteLineAsync(pending);

                while (pipe.IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync();
                    if (line is null)
                        break;

                    try
                    {
                        var message = JsonSerializer.Deserialize<BridgeMessage>(line);
                        if (message is not null)
                            MessageReceived?.Invoke(message);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Invalid companion command: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Companion pipe error: {ex.Message}");
                await Task.Delay(1000, cancellationToken);
            }
            finally
            {
                lock (_writerLock)
                    _writer = null;
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _worker?.Wait(1500); } catch { }
        _stop.Dispose();
    }
}
