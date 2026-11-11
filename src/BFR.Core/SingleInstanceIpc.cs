namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Dữ liệu truyền giữa các phiên thực thi qua Named Pipe.
/// </summary>
public sealed class IpcPayload
{
    public List<string> Paths { get; set; } = new();
    public string? FileListPath { get; set; }
    public string? PresetName { get; set; }

    /// <summary>Khoá preset đổi tên nhanh (--quick): áp dụng ngay, không mở cửa sổ chính.</summary>
    public string? QuickPreset { get; set; }
}

/// <summary>
/// Quản lý Mutex đơn phiên cho ứng dụng.
/// </summary>
public sealed class SingleInstanceLock : IDisposable
{
    public const string DefaultMutexName = @"Local\BatchFileRenamer_SingleInstance_Mutex";
    private readonly Mutex _mutex;
    private readonly bool _hasHandle;

    public bool IsPrimary => _hasHandle;

    public SingleInstanceLock(string mutexName = DefaultMutexName)
    {
        // WaitOne(0) thay vì createdNew: nếu instance trước bị crash, mutex có thể
        // bị bỏ rơi (abandoned) và instance mới vẫn phải trở thành primary.
        _mutex = new Mutex(false, mutexName);
        try
        {
            _hasHandle = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            _hasHandle = true;
        }
    }

    public void Dispose()
    {
        if (_hasHandle)
        {
            try { _mutex.ReleaseMutex(); } catch { /* ignore */ }
        }
        _mutex.Dispose();
    }
}

/// <summary>
/// Client IPC gửi danh sách tệp tới instance chính đang chạy.
/// </summary>
public static class SingleInstanceClient
{
    public const string DefaultPipeName = "BatchFileRenamer_IPC_Pipe";

    public static async Task<bool> TrySendAsync(
        IpcPayload payload,
        string pipeName = DefaultPipeName,
        int timeoutMs = 2000,
        CancellationToken ct = default)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            await client.ConnectAsync(cts.Token).ConfigureAwait(false);

            using var writer = new StreamWriter(client, leaveOpen: true);
            using var reader = new StreamReader(client, leaveOpen: true);

            var json = JsonSerializer.Serialize(payload);
            await writer.WriteLineAsync(json.AsMemory(), cts.Token).ConfigureAwait(false);
            await writer.FlushAsync(cts.Token).ConfigureAwait(false);

            var response = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
            return string.Equals(response, "ACK", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Server IPC lắng nghe trên Named Pipe và chuyển tiếp danh sách tệp nhận được vào luồng chính.
/// </summary>
public sealed class SingleInstanceServer : IAsyncDisposable
{
    public const string DefaultPipeName = "BatchFileRenamer_IPC_Pipe";
    private readonly string _pipeName;
    private readonly Action<IpcPayload> _onPayloadReceived;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenerTask;

    /// <summary>Thời gian tối đa chờ client gửi xong một tin nhắn.</summary>
    internal const int ReadTimeoutMs = 5000;

    private static readonly byte[] AckBytes = System.Text.Encoding.UTF8.GetBytes("ACK\n");

    /// <summary>Đọc một dòng nhưng dừng (trả null) nếu vượt <paramref name="maxChars"/> ký tự.</summary>
    internal static async Task<string?> ReadLineBoundedAsync(TextReader reader, int maxChars, CancellationToken ct)
    {
        var sb = new System.Text.StringBuilder();
        var buffer = new char[1];
        while (true)
        {
            var n = await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (n == 0) return sb.Length == 0 ? null : sb.ToString();
            if (buffer[0] == '\n') return sb.ToString();
            if (buffer[0] != '\r') sb.Append(buffer[0]);
            if (sb.Length > maxChars) return null;
        }
    }

    public SingleInstanceServer(Action<IpcPayload> onPayloadReceived, string pipeName = DefaultPipeName)
    {
        _pipeName = pipeName;
        _onPayloadReceived = onPayloadReceived ?? throw new ArgumentNullException(nameof(onPayloadReceived));
    }

    public void Start()
    {
        if (_listenerTask != null) return;
        _listenerTask = Task.Run(ListenLoopAsync);
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);

                using var reader = new StreamReader(server, leaveOpen: true);

                // Client im lặng hoặc gửi quá nhiều dữ liệu không được phép giữ server mãi mãi.
                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                readCts.CancelAfter(ReadTimeoutMs);

                string? line = null;
                try
                {
                    line = await ReadLineBoundedAsync(reader, PathGuard.MaxIpcMessageChars, readCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!_cts.IsCancellationRequested)
                {
                    // hết thời gian đọc: bỏ kết nối này
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    try
                    {
                        var payload = PathGuard.SanitizeIpcPayload(JsonSerializer.Deserialize<IpcPayload>(line));
                        _onPayloadReceived(payload);
                    }
                    catch
                    {
                        // định dạng không hợp lệ
                    }
                }

                // Pipe không có bộ đệm ghi: ghi tới client không chịu đọc sẽ treo mãi, nên chỉ phản hồi khi đã nhận được
                // tin nhắn, và luôn kèm thời gian chờ.
                if (line is not null)
                {
                    using var writeCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                    writeCts.CancelAfter(ReadTimeoutMs);
                    try
                    {
                        await server.WriteAsync(AckBytes, writeCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!_cts.IsCancellationRequested)
                    {
                        // client không nhận phản hồi: bỏ kết nối
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Lỗi kết nối tạm thời; đợi một chút rồi tiếp tục lắng nghe
                if (!_cts.IsCancellationRequested)
                {
                    await Task.Delay(100).ConfigureAwait(false);
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_listenerTask != null)
        {
            try { await _listenerTask.ConfigureAwait(false); } catch { /* ignore */ }
        }
        _cts.Dispose();
    }
}
