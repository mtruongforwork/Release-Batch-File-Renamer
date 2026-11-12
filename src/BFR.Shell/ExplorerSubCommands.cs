namespace BFR.Shell;

using BFR.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

/// <summary>
/// <c>IEnumExplorerCommand</c> phía .NET (CCW): Explorer dùng nó để duyệt các lệnh con của menu
/// "Batch Rename with BFR". Dùng con trỏ thô cho mảng kết quả để không phụ thuộc vào marshaller.
/// </summary>
[ComVisible(true)]
[Guid("a88826f8-186f-4987-aade-ea0cef8fbfe8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IEnumExplorerCommandServer
{
    /// <summary>Lấy tối đa <paramref name="celt"/> lệnh; ghi con trỏ IExplorerCommand (đã AddRef) vào <paramref name="rgelt"/>.</summary>
    [PreserveSig]
    int Next(uint celt, IntPtr rgelt, IntPtr pceltFetched);

    [PreserveSig]
    int Skip(uint celt);

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int Clone(out IntPtr ppenum);
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class EnumExplorerCommand : IEnumExplorerCommandServer
{
    private readonly IReadOnlyList<IExplorerCommandServer> _commands;
    private int _index;

    public EnumExplorerCommand(IReadOnlyList<IExplorerCommandServer> commands, int index = 0)
    {
        _commands = commands;
        _index = index;
    }

    public int Next(uint celt, IntPtr rgelt, IntPtr pceltFetched)
    {
        ComServerLifetime.Touch();
        if (rgelt == IntPtr.Zero) return HResult.E_INVALIDARG;

        uint fetched = 0;
        while (fetched < celt && _index < _commands.Count)
        {
            var unknown = Marshal.GetComInterfaceForObject(_commands[_index], typeof(IExplorerCommandServer));
            Marshal.WriteIntPtr(rgelt, (int)fetched * IntPtr.Size, unknown);   // người gọi sở hữu một tham chiếu
            _index++;
            fetched++;
        }

        if (pceltFetched != IntPtr.Zero) Marshal.WriteInt32(pceltFetched, (int)fetched);
        return fetched == celt ? HResult.S_OK : HResult.S_FALSE;
    }

    public int Skip(uint celt)
    {
        ComServerLifetime.Touch();
        _index = (int)Math.Min((long)_index + celt, _commands.Count);
        return _index < _commands.Count ? HResult.S_OK : HResult.S_FALSE;
    }

    public int Reset()
    {
        ComServerLifetime.Touch();
        _index = 0;
        return HResult.S_OK;
    }

    public int Clone(out IntPtr ppenum)
    {
        ComServerLifetime.Touch();
        ppenum = Marshal.GetComInterfaceForObject(new EnumExplorerCommand(_commands, _index), typeof(IEnumExplorerCommandServer));
        return HResult.S_OK;
    }
}

/// <summary>
/// Một lệnh con của menu: "Mở trong BFR" (khi <see cref="QuickKey"/> null) hoặc một preset đổi tên nhanh
/// (chạy <c>BatchFileRenamer.exe --quick &lt;khoá&gt;</c>, áp dụng ngay không mở cửa sổ chính).
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class ExplorerSubCommand : IExplorerCommandServer
{
    private readonly Guid _canonicalName;
    private readonly string _titleVi;
    private readonly string _titleEn;

    public ExplorerSubCommand(Guid canonicalName, string titleVi, string titleEn, string? quickKey, string? toolKey = null)
    {
        ToolKey = toolKey;
        _canonicalName = canonicalName;
        _titleVi = titleVi;
        _titleEn = titleEn;
        QuickKey = quickKey;
    }

    public string? QuickKey { get; }

    /// <summary>Khoá công cụ tệp: mở trang Công cụ với các tệp đã chọn.</summary>
    public string? ToolKey { get; }

    public string Title(bool vietnamese) => vietnamese ? _titleVi : _titleEn;

    private static bool IsVietnamese() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("vi", StringComparison.OrdinalIgnoreCase);

    public int GetTitle(IShellItemArray? psiItemArray, out IntPtr ppszName)
    {
        ComServerLifetime.Touch();
        ppszName = Marshal.StringToCoTaskMemUni(Title(IsVietnamese()));
        return HResult.S_OK;
    }

    public int GetIcon(IShellItemArray? psiItemArray, out IntPtr ppszIcon)
    {
        ComServerLifetime.Touch();
        ppszIcon = IntPtr.Zero;   // lệnh con không có biểu tượng riêng
        return HResult.E_NOTIMPL;
    }

    public int GetToolTip(IShellItemArray? psiItemArray, out IntPtr ppszInfotip)
    {
        ComServerLifetime.Touch();
        ppszInfotip = IntPtr.Zero;
        return HResult.E_NOTIMPL;
    }

    public int GetCanonicalName(out Guid pguidCommandName)
    {
        ComServerLifetime.Touch();
        pguidCommandName = _canonicalName;
        return HResult.S_OK;
    }

    public int GetState(IShellItemArray? psiItemArray, int fOkToBeSlow, out uint pCmdState)
    {
        ComServerLifetime.Touch();
        pCmdState = 0;   // ECS_ENABLED
        return HResult.S_OK;
    }

    public int Invoke(IShellItemArray? psiItemArray, IntPtr pbc)
    {
        ComServerLifetime.Touch();
        try
        {
            var paths = ExplorerCommand.CollectPaths(psiItemArray);
            return ExplorerCommandLauncher.Launch(paths, QuickKey, ToolKey);
        }
        catch
        {
            return HResult.E_FAIL;
        }
    }

    public int GetFlags(out uint pFlags)
    {
        ComServerLifetime.Touch();
        pFlags = 0;   // ECF_DEFAULT
        return HResult.S_OK;
    }

    public int EnumSubCommands(out IntPtr ppEnum)
    {
        ComServerLifetime.Touch();
        ppEnum = IntPtr.Zero;
        return HResult.E_NOTIMPL;
    }

    /// <summary>Danh sách lệnh con theo thứ tự hiển thị: Mở trong BFR, rồi các preset nhanh.</summary>
    public static IReadOnlyList<IExplorerCommandServer> BuildAll()
    {
        var list = new List<IExplorerCommandServer>
        {
            new ExplorerSubCommand(Guid.Parse("b7a3c5d1-0001-4e5f-8a6b-0f1e2d3c4b01"), "Mở trong BFR", "Open in BFR", null)
        };

        var id = 2;
        foreach (var quick in QuickPresets.All)
        {
            list.Add(new ExplorerSubCommand(
                Guid.Parse($"b7a3c5d1-{id++:x4}-4e5f-8a6b-0f1e2d3c4b01"),
                quick.TitleVi,
                quick.TitleEn,
                quick.Key));
        }

        // Công cụ tệp v2.0: mở trang Công cụ với các tệp đã chọn (người dùng xem trước rồi mới áp dụng).
        var tools = new (string Key, string Vi, string En)[]
        {
            ("organize", "Sắp xếp vào thư mục…", "Organize into folders…"),
            ("duplicates", "Tìm tệp trùng…", "Find duplicates…"),
            ("metadata", "Ngày giờ / xoá GPS…", "Dates / remove GPS…"),
            ("archive", "Nén ZIP…", "Compress to ZIP…"),
            ("checksum", "Tính checksum…", "Compute checksum…")
        };
        foreach (var (key, vi, en) in tools)
        {
            list.Add(new ExplorerSubCommand(
                Guid.Parse($"b7a3c5d1-{id++:x4}-4e5f-8a6b-0f1e2d3c4b01"), vi, en, null, key));
        }

        return list;
    }
}
