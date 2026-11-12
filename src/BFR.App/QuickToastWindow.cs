namespace BFR.App;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Ui = Wpf.Ui.Controls;

/// <summary>
/// Thông báo nhỏ ở góc màn hình sau khi đổi tên nhanh từ menu chuột phải (B1): nói rõ kết quả và có
/// nút Hoàn tác. Tự đóng sau vài giây (dừng đếm khi rê chuột vào). Không lấy focus của cửa sổ khác.
/// </summary>
public sealed class QuickToastWindow : Window
{
    private readonly DispatcherTimer _timer;
    private readonly TextBlock _message;
    private readonly Ui.Button _undoButton;

    /// <param name="message">Nội dung kết quả.</param>
    /// <param name="undo">Hàm hoàn tác trả về thông báo mới; null nếu không có gì để hoàn tác.</param>
    /// <param name="isProblem">True nếu là cảnh báo/lỗi (đổi biểu tượng, giữ lâu hơn).</param>
    public QuickToastWindow(string message, Func<string>? undo, bool isProblem, int index)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        System.Windows.Automation.AutomationProperties.SetName(this, "Quick rename result");

        var accent = (Brush)Application.Current.FindResource(isProblem ? "Warning" : "Success");

        var glyph = new TextBlock
        {
            Text = isProblem ? "" : "",
            FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
            FontSize = 18,
            Foreground = accent,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 12, 0)
        };

        _message = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 320,
            Foreground = (Brush)Application.Current.FindResource("TextPrimary"),
            VerticalAlignment = VerticalAlignment.Center
        };

        _undoButton = new Ui.Button
        {
            Content = Loc.T("QuickUndo"),
            Appearance = Ui.ControlAppearance.Secondary,
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = undo is null ? Visibility.Collapsed : Visibility.Visible
        };
        System.Windows.Automation.AutomationProperties.SetName(_undoButton, "Undo quick rename");

        var close = new Ui.Button
        {
            Content = "",
            FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
            Appearance = Ui.ControlAppearance.Transparent,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(6),
            VerticalAlignment = VerticalAlignment.Top
        };
        close.Click += (_, _) => Close();

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(glyph);
        row.Children.Add(_message);
        row.Children.Add(_undoButton);
        row.Children.Add(close);

        Content = new Border
        {
            Margin = new Thickness(12),   // chỗ cho bóng đổ
            Padding = new Thickness(16, 12, 10, 12),
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.FindResource("SurfaceBackground"),
            BorderBrush = (Brush)Application.Current.FindResource("Divider"),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.28, Color = Colors.Black },
            Child = row
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(isProblem ? 14 : 9) };
        _timer.Tick += (_, _) => Close();
        MouseEnter += (_, _) => _timer.Stop();
        MouseLeave += (_, _) => { _timer.Stop(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();

        if (undo is not null)
        {
            _undoButton.Click += (_, _) =>
            {
                _undoButton.IsEnabled = false;
                _message.Text = undo();
                _undoButton.Visibility = Visibility.Collapsed;
                _timer.Interval = TimeSpan.FromSeconds(4);
                _timer.Stop();
                _timer.Start();
            };
        }

        Loaded += (_, _) =>
        {
            // Góc dưới bên phải vùng làm việc; các thông báo xếp chồng lên nhau.
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 8;
            Top = area.Bottom - ActualHeight - 8 - index * (ActualHeight + 4);
            _timer.Start();
        };
    }
}
