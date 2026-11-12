namespace BFR.App;

using BFR.Core.Licensing;
using BFR.Presentation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

/// <summary>
/// Khay nổi: thả tệp từ nhiều thư mục vào đây rồi đổi tên cùng lúc.
/// Logic nằm trong <see cref="ShelfModel"/>; cửa sổ chỉ nối view với model.
/// </summary>
public partial class ShelfWindow : Wpf.Ui.Controls.FluentWindow
{
    private sealed record ShelfRow(string Name, string FullPath);

    private readonly ShelfModel _model;
    private readonly Action<IReadOnlyList<string>> _sendToMain;
    private readonly Action<ProFeature> _onLimitReached;

    public ShelfWindow(
        ShelfModel model,
        Action<IReadOnlyList<string>> sendToMain,
        Action<ProFeature> onLimitReached)
    {
        InitializeComponent();

        _model = model;
        _sendToMain = sendToMain;
        _onLimitReached = onLimitReached;

        Title = Loc.T("ShelfTitle");
        ClearButton.Content = Loc.T("ShelfClear");
        RenameButton.Content = Loc.T("ShelfRename");
        HintText.Text = Loc.T("ShelfHint");

        _model.Items.CollectionChanged += (_, _) => Refresh();
        Closed += (_, _) => _autoClose?.Stop();
        Refresh();
    }

    private System.Windows.Threading.DispatcherTimer? _autoClose;

    /// <summary>
    /// Tự đóng nếu sau <paramref name="delay"/> mà khay vẫn trống, chuột không ở trên khay và
    /// người dùng không còn giữ nút trái (đang kéo). Dùng cho Shelf mở bằng lắc chuột.
    /// </summary>
    public void AutoCloseWhenUnused(TimeSpan delay)
    {
        _autoClose?.Stop();
        _autoClose = new System.Windows.Threading.DispatcherTimer { Interval = delay };
        _autoClose.Tick += (_, _) =>
        {
            if (!_model.IsEmpty) { _autoClose?.Stop(); return; }          // đã có tệp: giữ lại
            if (IsMouseOver || BFR.App.Infrastructure.MouseHook.IsLeftButtonDown) return;   // chờ tiếp
            _autoClose?.Stop();
            Close();
        };
        _autoClose.Start();
    }

    /// <summary>Thêm đường dẫn từ bên ngoài (ví dụ lệnh trong cửa sổ chính).</summary>
    public void AddPaths(IEnumerable<string> paths)
    {
        var result = _model.Add(paths);
        if (result.Added > 0) _autoClose?.Stop();
        if (result.HitLimit) _onLimitReached(ProFeature.UnlimitedShelf);
    }

    private void Refresh()
    {
        ItemList.ItemsSource = _model.Items
            .Select(p => new ShelfRow(Path.GetFileName(p.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : p, p))
            .ToList();

        var limit = _model.Limit == int.MaxValue ? "∞" : _model.Limit.ToString();
        CountText.Text = $"{_model.Count} / {limit}";
        HintText.Visibility = _model.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        RenameButton.IsEnabled = !_model.IsEmpty;
        ClearButton.IsEnabled = !_model.IsEmpty;
    }

    private void Shelf_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Shelf_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddPaths(paths);
    }

    private void ItemList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete) return;

        foreach (var row in ItemList.SelectedItems.OfType<ShelfRow>().ToList())
        {
            _model.Remove(row.FullPath);
        }
        e.Handled = true;
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => _model.Clear();

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (_model.IsEmpty) return;

        _sendToMain(_model.Snapshot());
        _model.Clear();
    }
}
