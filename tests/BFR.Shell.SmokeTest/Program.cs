namespace BFR.Shell.SmokeTest;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

/// <summary>
/// Kiểm thử khói cho COM local server đã đóng gói. Chạy sau khi đăng ký
/// BFR.Shell.exe bằng <c>bfr context-menu register</c>.
/// </summary>
public static class Program
{
    private const string ExplorerCommandClsid = "6B0F3C2A-7E4D-4A8B-9C1D-2F5E8A7B6C4D";

    public static int Main()
    {
        try
        {
            var clsid = Guid.Parse(ExplorerCommandClsid);
            var type = Type.GetTypeFromCLSID(clsid, throwOnError: true);
            var instance = Activator.CreateInstance(type!);
            if (instance is not IExplorerCommand command)
            {
                Console.Error.WriteLine("Không tạo được IExplorerCommand.");
                return 1;
            }

            int hr = command.GetTitle(IntPtr.Zero, out var titlePtr);
            RequireHr("GetTitle", hr);
            var title = Marshal.PtrToStringUni(titlePtr);
            if (titlePtr != IntPtr.Zero) Marshal.FreeCoTaskMem(titlePtr);
            if (string.IsNullOrWhiteSpace(title))
            {
                Console.Error.WriteLine("GetTitle trả về chuỗi rỗng.");
                return 1;
            }

            hr = command.GetIcon(IntPtr.Zero, out var iconPtr);
            RequireHr("GetIcon", hr);
            var icon = Marshal.PtrToStringUni(iconPtr);
            if (iconPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(iconPtr);
            if (string.IsNullOrWhiteSpace(icon))
            {
                Console.Error.WriteLine("GetIcon không tìm thấy BatchFileRenamer.exe cạnh BFR.Shell.exe.");
                return 1;
            }

            hr = command.GetState(IntPtr.Zero, 1, out var state);
            RequireHr("GetState", hr);
            if (state != 0)
            {
                Console.Error.WriteLine($"GetState trả về {state}, mong đợi 0 (ECS_ENABLED).");
                return 1;
            }

            hr = command.GetFlags(out var flags);
            RequireHr("GetFlags", hr);
            const uint EcfHasSubCommands = 0x1;
            if (flags != EcfHasSubCommands)
            {
                Console.Error.WriteLine($"GetFlags trả về {flags}, mong đợi {EcfHasSubCommands} (ECF_HASSUBCOMMANDS).");
                return 1;
            }

            var subResult = CheckSubCommands(command);
            if (subResult != 0) return subResult;

            hr = command.GetCanonicalName(out var canonical);
            RequireHr("GetCanonicalName", hr);
            if (canonical != clsid)
            {
                Console.Error.WriteLine($"GetCanonicalName trả về {canonical}, mong đợi {clsid}.");
                return 1;
            }

            Console.WriteLine($"COM smoke test PASS: title=\"{title}\", icon=\"{icon}\"");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("COM smoke test FAIL: " + ex);
            return 1;
        }
    }

    /// <summary>
    /// Duyệt menu con qua COM thật: Next/Skip/Reset/Clone, tiêu đề, cờ, tên chuẩn, và Next một lần lấy cả danh sách.
    /// </summary>
    private static int CheckSubCommands(IExplorerCommand parent)
    {
        string[] expectedKeys = { "Open", "number", "ascii", "date", "clean", "lower" };

        int hr = parent.EnumSubCommands(out var enumPtr);
        RequireHr("EnumSubCommands", hr);
        if (enumPtr == IntPtr.Zero) { Console.Error.WriteLine("EnumSubCommands trả về con trỏ rỗng."); return 1; }

        var enumerator = (IEnumExplorerCommand)Marshal.GetObjectForIUnknown(enumPtr);
        Marshal.Release(enumPtr);

        var titles = new List<string>();
        var names = new HashSet<Guid>();
        while (true)
        {
            hr = enumerator.Next(1, out var child, out var fetched);
            if (hr == 1 || fetched == 0) break;   // S_FALSE: hết danh sách
            RequireHr("Next", hr);

            RequireHr("child.GetTitle", child.GetTitle(IntPtr.Zero, out var tp));
            var t = Marshal.PtrToStringUni(tp);
            if (tp != IntPtr.Zero) Marshal.FreeCoTaskMem(tp);
            if (string.IsNullOrWhiteSpace(t)) { Console.Error.WriteLine("Lệnh con có tiêu đề rỗng."); return 1; }
            titles.Add(t);

            RequireHr("child.GetFlags", child.GetFlags(out var cf));
            if (cf != 0) { Console.Error.WriteLine($"Lệnh con '{t}' có cờ {cf}, mong đợi 0."); return 1; }

            RequireHr("child.GetState", child.GetState(IntPtr.Zero, 1, out var cs));
            if (cs != 0) { Console.Error.WriteLine($"Lệnh con '{t}' có state {cs}, mong đợi 0."); return 1; }

            RequireHr("child.GetCanonicalName", child.GetCanonicalName(out var g));
            if (!names.Add(g)) { Console.Error.WriteLine($"Tên chuẩn trùng: {g}."); return 1; }

            if (child.EnumSubCommands(out var none) >= 0 || none != IntPtr.Zero)
            {
                Console.Error.WriteLine($"Lệnh con '{t}' không được có menu con.");
                return 1;
            }
        }

        if (titles.Count != expectedKeys.Length)
        {
            Console.Error.WriteLine($"Mong đợi {expectedKeys.Length} lệnh con, nhận {titles.Count}: {string.Join(" | ", titles)}");
            return 1;
        }

        // Reset rồi lấy cả danh sách trong MỘT lần gọi Next.
        RequireHr("Reset", enumerator.Reset());
        var all = new IExplorerCommand[expectedKeys.Length + 2];
        hr = ((IEnumExplorerCommandMany)enumerator).Next((uint)all.Length, all, out var got);
        if (got != expectedKeys.Length || hr != 1)
        {
            Console.Error.WriteLine($"Next({all.Length}) trả về {got} phần tử, hr={hr}; mong đợi {expectedKeys.Length} và S_FALSE.");
            return 1;
        }

        // Skip và Clone giữ đúng vị trí.
        RequireHr("Reset", enumerator.Reset());
        RequireHr("Skip", enumerator.Skip(2));
        RequireHr("Clone", enumerator.Clone(out var cloneObj));
        hr = cloneObj.Next(1, out var third, out var f3);
        RequireHr("Clone.Next", hr);
        third.GetTitle(IntPtr.Zero, out var tp3);
        var thirdTitle = Marshal.PtrToStringUni(tp3);
        if (tp3 != IntPtr.Zero) Marshal.FreeCoTaskMem(tp3);
        if (thirdTitle != titles[2])
        {
            Console.Error.WriteLine($"Sau Skip(2)+Clone, phần tử đầu là '{thirdTitle}', mong đợi '{titles[2]}'.");
            return 1;
        }

        Console.WriteLine($"Menu con PASS: {titles.Count} lệnh: {string.Join(" | ", titles)}");
        return 0;
    }

    private static void RequireHr(string method, int hr)
    {
        if (hr < 0)
        {
            throw new InvalidOperationException($"{method} thất bại: 0x{hr:X8}");
        }
    }
}

[ComImport]
[Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExplorerCommand
{
    [PreserveSig] int GetTitle(IntPtr psiItemArray, out IntPtr ppszName);
    [PreserveSig] int GetIcon(IntPtr psiItemArray, out IntPtr ppszIcon);
    [PreserveSig] int GetToolTip(IntPtr psiItemArray, out IntPtr ppszInfotip);
    [PreserveSig] int GetCanonicalName(out Guid pguidCommandName);
    [PreserveSig] int GetState(IntPtr psiItemArray, int fOkToBeSlow, out uint pCmdState);
    [PreserveSig] int Invoke(IntPtr psiItemArray, IntPtr pbc);
    [PreserveSig] int GetFlags(out uint pFlags);
    [PreserveSig] int EnumSubCommands(out IntPtr ppEnum);
}

[ComImport]
[Guid("a88826f8-186f-4987-aade-ea0cef8fbfe8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IEnumExplorerCommand
{
    [PreserveSig] int Next(uint celt, [MarshalAs(UnmanagedType.Interface)] out IExplorerCommand pUICommand, out uint pceltFetched);
    [PreserveSig] int Skip(uint celt);
    [PreserveSig] int Reset();
    [PreserveSig] int Clone([MarshalAs(UnmanagedType.Interface)] out IEnumExplorerCommand ppenum);
}

/// <summary>Cùng IID với IEnumExplorerCommand nhưng Next nhận mảng, để kiểm tra lấy nhiều lệnh trong một lần gọi.</summary>
[ComImport]
[Guid("a88826f8-186f-4987-aade-ea0cef8fbfe8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IEnumExplorerCommandMany
{
    [PreserveSig] int Next(uint celt, [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.Interface, SizeParamIndex = 0)] IExplorerCommand[] rgelt, out uint pceltFetched);
}
