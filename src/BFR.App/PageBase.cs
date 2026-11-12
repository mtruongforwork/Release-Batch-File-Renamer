namespace BFR.App;

using System;
using System.Windows.Controls;

/// <summary>
/// Nền chung cho các trang trong khung điều hướng của cửa sổ chính (thay cho các cửa sổ phụ cũ).
/// Trang được tạo một lần, ẩn/hiện khi chuyển trang; <see cref="OnShown"/> chạy mỗi lần trang hiện ra.
/// </summary>
public abstract class PageBase : UserControl
{
    /// <summary>Trang muốn quay về trang Đổi tên (ví dụ sau khi áp dụng một preset).</summary>
    public event EventHandler? CloseRequested;

    protected void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Gọi mỗi khi trang được hiển thị: nạp lại dữ liệu để luôn mới.</summary>
    public virtual void OnShown() { }

    /// <summary>Gọi khi người dùng rời trang.</summary>
    public virtual void OnHidden() { }

    /// <summary>Gọi khi cửa sổ chính đóng thật: dừng các tác vụ nền của trang.</summary>
    public virtual void Shutdown() { }
}
