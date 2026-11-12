namespace WpfUiTrial;

using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Grid.ItemsSource = Enumerable.Range(1, 12)
            .Select(i => new { Cu = $"IMG_{i:0000}.JPG", Moi = $"2026-11-05_{i:00}.jpg", TrangThai = "Sẽ đổi" })
            .ToList();
    }

    private void Theme_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var next = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark
            ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(next);
    }
}
