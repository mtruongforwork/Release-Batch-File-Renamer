namespace BFR.Shell;

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using BFR.Core;

/// <summary>
/// Class factory managed dùng cho COM local server. Interface này không phải
/// <see cref="ComImportAttribute"/> vì phía .NET tạo CCW để COM gọi vào.
/// </summary>
[ComVisible(true)]
[Guid("00000001-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IClassFactoryServer
{
    [PreserveSig]
    int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);

    [PreserveSig]
    int LockServer(bool fLock);
}

[ComVisible(true)]
public sealed class ExplorerCommandFactory : IClassFactoryServer
{
    private int _locks;

    public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
    {
        ppvObject = IntPtr.Zero;
        ComServerLifetime.Touch();

        // Không hỗ trợ aggregation; Explorer luôn tạo object độc lập.
        if (pUnkOuter != IntPtr.Zero) return unchecked((int)0x80040110); // CLASS_E_NOAGGREGATION

        var command = new ExplorerCommandServer();
        IntPtr unknown = Marshal.GetComInterfaceForObject(command, typeof(IExplorerCommandServer));
        try
        {
            int hr = Marshal.QueryInterface(unknown, ref riid, out ppvObject);
            ComServerLifetime.Log($"CreateInstance riid={riid} hr=0x{hr:X8}");
            return hr;
        }
        catch (Exception ex)
        {
            ComServerLifetime.Log("CreateInstance failed: " + ex);
            return unchecked((int)0x80004005); // E_FAIL
        }
        finally
        {
            if (unknown != IntPtr.Zero) Marshal.Release(unknown);
        }
    }

    public int LockServer(bool fLock)
    {
        if (fLock) Interlocked.Increment(ref _locks);
        else Interlocked.Decrement(ref _locks);
        ComServerLifetime.Touch();
        return 0;
    }
}

/// <summary>
/// COM local server (out-of-process). Dùng exe thay vì comhost để có thể publish
/// self-contained trong MSIX mà không phụ thuộc .NET runtime cài sẵn trên máy.
/// </summary>
public static class ComServerLifetime
{
    private const int RegClsMultipleUse = 1;
    private const int RegClsSuspended = 4;
    private const int ClsCtxLocalServer = 4;
    private const int SFalse = 1;

    private static long _lastActivityTicks = DateTime.UtcNow.Ticks;

    public static void Touch() =>
        Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);

    public static TimeSpan IdleTime
    {
        get
        {
            var last = new DateTime(Interlocked.Read(ref _lastActivityTicks), DateTimeKind.Utc);
            return DateTime.UtcNow - last;
        }
    }

    public static void Log(string message)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("BFR_SHELL_LOG"), "1", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var directory = Path.Combine(Path.GetTempPath(), "BatchFileRenamer", "shell");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "shell-server.log"),
                $"{DateTime.Now:O} [pid {Environment.ProcessId}] {BFR.Core.LogRedactor.Redact(message)}{Environment.NewLine}");
        }
        catch
        {
            // Chẩn đoán không được làm hỏng menu Explorer.
        }
    }

    [MTAThread]
    public static int Main(string[] args)
    {
        Log("start " + string.Join(' ', args));
        int hr = CoInitializeEx(IntPtr.Zero, 0 /* COINIT_MULTITHREADED */);
        if (hr < 0 && hr != SFalse)
        {
            Log($"CoInitializeEx failed 0x{hr:X8}");
            return hr;
        }

        var clsid = Guid.Parse(ShellIntegration.ExplorerCommandClsid);
        var factory = new ExplorerCommandFactory();

        hr = CoRegisterClassObject(
            ref clsid,
            factory,
            ClsCtxLocalServer,
            RegClsMultipleUse | RegClsSuspended,
            out uint cookie);

        if (hr < 0)
        {
            Log($"CoRegisterClassObject failed 0x{hr:X8}");
            CoUninitialize();
            return hr;
        }

        hr = CoResumeClassObjects();
        Log($"registered cookie={cookie} resume=0x{hr:X8}");
        Touch();

        // Tự thoát sau 60 giây không có lời gọi. Explorer sẽ khởi động lại
        // server khi người dùng mở menu lần sau.
        while (IdleTime < TimeSpan.FromSeconds(60))
        {
            Thread.Sleep(500);
        }

        Log("idle exit");
        CoRevokeClassObject(cookie);
        CoUninitialize();
        return 0;
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(
        ref Guid rclsid,
        [MarshalAs(UnmanagedType.Interface)] IClassFactoryServer pUnk,
        uint dwClsContext,
        uint flags,
        out uint lpdwRegister);

    [DllImport("ole32.dll")]
    private static extern int CoResumeClassObjects();

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint dwRegister);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
}
