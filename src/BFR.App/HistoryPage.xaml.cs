namespace BFR.App;

using BFR.Core;
using BFR.Core.Licensing;
using System;
using System.Linq;
using System.Windows;

public partial class HistoryPage : PageBase
{
    /// <summary>Trang bị rời đi: cửa sổ chính tính lại xem trước (tên tệp có thể đã được hoàn tác).</summary>
    public event EventHandler? Left;

    public override void OnShown() => Refresh();

    public override void OnHidden() => Left?.Invoke(this, EventArgs.Empty);

    private readonly HistoryStore _history;
    private readonly IFileSystem _fs;
    private readonly LicenseService _license;

    public HistoryPage(HistoryStore history, IFileSystem fs, LicenseService? license = null)
    {
        InitializeComponent();
        _history = history;
        _fs = fs;
        _license = license ?? new LicenseService(new LicenseStore(new RealFileSystem()), trialEnabled: false);

        HistoryGrid.Columns[0].Header = Loc.T("HistoryColId");
        HistoryGrid.Columns[1].Header = Loc.T("HistoryColTime");
        HistoryGrid.Columns[2].Header = Loc.T("HistoryColTotal");
        HistoryGrid.Columns[3].Header = Loc.T("HistoryColOk");
        HistoryGrid.Columns[4].Header = Loc.T("HistoryColFailed");
        HistoryGrid.Columns[5].Header = Loc.T("HistoryColStatus");
        HistoryGrid.Columns[6].Header = Loc.T("HistoryColPreset");

        Refresh();
    }

    private void Refresh()
    {
        // Pro: giữ lịch sử 30 ngày; Free: chỉ 1 lần gần nhất.
        var entries = _history.LoadAll()
            .Take(_license.MaxUndoEntries == int.MaxValue ? int.MaxValue : _license.MaxUndoEntries)
            .ToList();

        HistoryGrid.ItemsSource = entries;
        EmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not HistoryEntry entry)
        {
            MessageBox.Show(Loc.T("SelectHistoryFirst"), Loc.T("UndoTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = new UndoService(_fs).UndoJournal(entry.JournalPath);
        var message = Loc.F("UndoMessage", result.Undone, result.Total)
            + (result.Failed > 0 ? Loc.F("UndoPartial", result.Failed) : string.Empty);

        MessageBox.Show(message, Loc.T("UndoTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        Refresh();
    }
}
