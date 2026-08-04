using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using ScheduleICompanion.Shared;

namespace ScheduleICompanion.App;

public sealed class PipeClient : IAsyncDisposable
{
    public const string PipeName = "ScheduleICompanion.v1";
    private readonly CancellationTokenSource _stop = new();
    private readonly object _writerLock = new();
    private StreamWriter? _writer;
    private Task? _worker;

    public event Action<bool>? ConnectionChanged;
    public event Action<BridgeMessage>? MessageReceived;
    public event Action<string>? Diagnostic;

    public void Start() => _worker ??= Task.Run(() => RunAsync(_stop.Token));

    public void Stop()
    {
        if (!_stop.IsCancellationRequested)
            _stop.Cancel();
    }

    public bool Send(string type, object? payload = null)
    {
        var message = JsonSerializer.Serialize(new BridgeMessage { Type = type, Payload = payload });
        lock (_writerLock)
        {
            if (_writer is null)
                return false;

            try
            {
                _writer.WriteLine(message);
                _writer.Flush();
                return true;
            }
            catch (Exception ex)
            {
                Diagnostic?.Invoke($"Command send failed: {ex.Message}");
                _writer = null;
                return false;
            }
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(
                    ".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

                await pipe.ConnectAsync(1500, cancellationToken);

                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
                {
                    AutoFlush = true
                };

                lock (_writerLock)
                    _writer = writer;
                ConnectionChanged?.Invoke(true);

                while (pipe.IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
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
                        Diagnostic?.Invoke($"Invalid bridge message: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Diagnostic?.Invoke($"Connection retry: {ex.Message}");
            }
            finally
            {
                lock (_writerLock)
                    _writer = null;
                ConnectionChanged?.Invoke(false);
            }

            try { await Task.Delay(1000, cancellationToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        if (_worker is not null)
        {
            try { await _worker; } catch { }
        }
        _stop.Dispose();
    }
}
