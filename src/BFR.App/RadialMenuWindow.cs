namespace BFR.App;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using BFR.Presentation.Tools;

/// <summary>
/// Menu tròn nhỏ gọn mở tại con trỏ: 8 ô, mỗi ô một công cụ.
/// Rê chuột vào ô thì ô nổi lên và hiện tên ở giữa; bấm chuột phải thì bung các chức năng của công cụ đó;
/// bấm chuột trái mở công cụ. Bàn phím: mũi tên chọn ô, Enter mở, Space bung chức năng, 1–8 mở nhanh, Esc đóng.
/// </summary>
internal sealed class RadialMenuWindow : Window
{
    private const double Outer = 150, Inner = 52, Pad = 16, FlyoutWidth = 280, Hover = 9;

    private readonly IReadOnlyList<RadialItem> _items;
    private readonly Action<RadialItem, RadialAction?> _activate;
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent };
    private readonly List<Path> _wedges = new();
    private readonly List<TextBlock> _icons = new();
    private readonly List<TranslateTransform> _offsets = new();
    private readonly TextBlock _hubIcon = new();
    private readonly TextBlock _hubTitle = new();
    private readonly TextBlock _hint = new();
    private readonly double _cx, _cy;
    private Border? _flyout;
    private int _hovered = -1;
    private bool _closing;

    public RadialMenuWindow(IReadOnlyList<RadialItem> items, Action<RadialItem, RadialAction?> activate)
    {
        _items = items;
        _activate = activate;

        Width = 2 * (Outer + Pad + FlyoutWidth);
        Height = 2 * (Outer + Pad) + 70;
        _cx = Width / 2;
        _cy = Pad + Outer;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = true;
        System.Windows.Automation.AutomationProperties.SetName(this, "Radial menu");

        Build();
        Content = _canvas;

        _canvas.MouseMove += (_, e) => OnMove(e.GetPosition(_canvas));
        _canvas.MouseLeave += (_, _) => { if (_flyout is null) SetHover(-1); };
        _canvas.MouseLeftButtonUp += OnLeftUp;
        _canvas.MouseRightButtonUp += OnRightUp;
        PreviewKeyDown += OnKey;
        Deactivated += (_, _) => CloseMenu();
    }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    // ----------------------------------------------------------------- Dựng hình

    private void Build()
    {
        var count = _items.Count;
        for (var i = 0; i < count; i++)
        {
            var path = new Path
            {
                Data = Sector(i, count),
                Fill = Res("CardBackground"),
                Stroke = Res("BorderStrong"),
                StrokeThickness = 1,
                RenderTransform = new TranslateTransform(),
                IsHitTestVisible = false
            };
            _offsets.Add((TranslateTransform)path.RenderTransform);
            _wedges.Add(path);
            _canvas.Children.Add(path);

            var (ix, iy) = RadialLayout.Point(_cx, _cy, (Inner + Outer) / 2, RadialLayout.CenterAngle(i, count));
            var icon = new TextBlock
            {
                Text = _items[i].Icon,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 24,
                Foreground = Res("Accent"),
                IsHitTestVisible = false,
                RenderTransform = new TranslateTransform()
            };
            icon.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(icon, ix - icon.DesiredSize.Width / 2);
            Canvas.SetTop(icon, iy - icon.DesiredSize.Height / 2);
            _icons.Add(icon);
            _canvas.Children.Add(icon);

            // Phím số mở nhanh: hiện số nhỏ ở góc ô.
            var (nx, ny) = RadialLayout.Point(_cx, _cy, Outer - 14, RadialLayout.CenterAngle(i, count));
            var number = new TextBlock { Text = (i + 1).ToString(), FontSize = 10, Foreground = Res("TextSecondary"), IsHitTestVisible = false };
            Canvas.SetLeft(number, nx - 3);
            Canvas.SetTop(number, ny - 7);
            _canvas.Children.Add(number);
        }

        // Lỗ giữa: tên công cụ đang rê chuột.
        var hub = new Ellipse
        {
            Width = 2 * (Inner - 5),
            Height = 2 * (Inner - 5),
            Fill = Res("PanelBackground"),
            Stroke = Res("BorderStrong"),
            StrokeThickness = 1,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(hub, _cx - (Inner - 5));
        Canvas.SetTop(hub, _cy - (Inner - 5));
        _canvas.Children.Add(hub);

        _hubIcon.FontFamily = new FontFamily("Segoe MDL2 Assets");
        _hubIcon.FontSize = 20;
        _hubIcon.Foreground = Res("Accent");
        _hubIcon.TextAlignment = TextAlignment.Center;
        _hubIcon.Width = 2 * (Inner - 9);
        _hubIcon.IsHitTestVisible = false;
        Canvas.SetLeft(_hubIcon, _cx - (Inner - 9));
        Canvas.SetTop(_hubIcon, _cy - 28);
        _canvas.Children.Add(_hubIcon);

        _hubTitle.FontSize = 11;
        _hubTitle.FontWeight = FontWeights.SemiBold;
        _hubTitle.Foreground = Res("TextPrimary");
        _hubTitle.TextAlignment = TextAlignment.Center;
        _hubTitle.TextWrapping = TextWrapping.Wrap;
        _hubTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        _hubTitle.MaxHeight = 30;
        _hubTitle.Width = 2 * (Inner - 12);
        _hubTitle.IsHitTestVisible = false;
        Canvas.SetLeft(_hubTitle, _cx - (Inner - 12));
        Canvas.SetTop(_hubTitle, _cy + 1);
        _canvas.Children.Add(_hubTitle);

        _hint.Text = Loc.T("RadialHint");
        _hint.FontSize = 11;
        _hint.Foreground = Res("TextSecondary");
        _hint.TextAlignment = TextAlignment.Center;
        _hint.Width = 2 * Outer;
        _hint.IsHitTestVisible = false;
        Canvas.SetLeft(_hint, _cx - Outer);
        Canvas.SetTop(_hint, _cy + Outer + 12);
        _canvas.Children.Add(_hint);

        _canvas.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.45, Color = Colors.Black };
        SetHover(-1);
    }

    /// <summary>Hình quạt (một phần của vành khuyên) cho ô <paramref name="index"/>, chừa khe nhỏ giữa các ô.</summary>
    private Geometry Sector(int index, int count)
    {
        var centre = RadialLayout.CenterAngle(index, count);
        var half = 360.0 / count / 2 - 1.2;
        var a0 = centre - half;
        var a1 = centre + half;

        var (x0, y0) = RadialLayout.Point(_cx, _cy, Outer, a0);
        var (x1, y1) = RadialLayout.Point(_cx, _cy, Outer, a1);
        var (x2, y2) = RadialLayout.Point(_cx, _cy, Inner, a1);
        var (x3, y3) = RadialLayout.Point(_cx, _cy, Inner, a0);

        var figure = new PathFigure { StartPoint = new Point(x0, y0), IsClosed = true };
        figure.Segments.Add(new ArcSegment(new Point(x1, y1), new Size(Outer, Outer), 0, false, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(new Point(x2, y2), true));
        figure.Segments.Add(new ArcSegment(new Point(x3, y3), new Size(Inner, Inner), 0, false, SweepDirection.Counterclockwise, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    // ----------------------------------------------------------------- Rê chuột và bấm

    private int IndexAt(Point p) => RadialLayout.IndexAt(p.X - _cx, p.Y - _cy, Inner, Outer + Hover, _items.Count);

    private void OnMove(Point p)
    {
        // Đang bung chức năng thì giữ nguyên ô đang chọn để người dùng di chuột sang danh sách.
        if (_flyout is not null) return;
        SetHover(IndexAt(p));
    }

    private void SetHover(int index)
    {
        if (index == _hovered && _hubTitle.Text.Length > 0) return;
        _hovered = index;

        for (var i = 0; i < _wedges.Count; i++)
        {
            var on = i == index;
            _wedges[i].Fill = on ? Res("Accent") : Res("CardBackground");
            _icons[i].Foreground = on ? Res("TextOnAccent") : Res("Accent");
            Panel.SetZIndex(_wedges[i], on ? 2 : 0);
            Panel.SetZIndex(_icons[i], on ? 3 : 1);

            // Ô được chọn "nổi lên": dịch ra ngoài theo hướng của nó.
            var (dx, dy) = on ? RadialLayout.Point(0, 0, Hover, RadialLayout.CenterAngle(i, _items.Count)) : (0.0, 0.0);
            Animate(_offsets[i], dx, dy);
            Animate((TranslateTransform)_icons[i].RenderTransform, dx, dy);
        }

        if (index >= 0)
        {
            _hubIcon.Text = _items[index].Icon;
            _hubTitle.Text = Loc.T(_items[index].TitleKey);
        }
        else
        {
            _hubIcon.Text = string.Empty;
            _hubTitle.Text = string.Empty;
        }
    }

    private static void Animate(TranslateTransform transform, double x, double y)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(110));
        transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, duration));
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, duration));
    }

    private void OnLeftUp(object sender, MouseButtonEventArgs e)
    {
        var index = IndexAt(e.GetPosition(_canvas));
        if (_flyout is not null && !IsInside(_flyout, e)) { HideFlyout(); }
        else if (_flyout is not null) return;

        if (index < 0) { CloseMenu(); return; }

        // Bấm chuột trái: mở công cụ.
        Choose(_items[index], null);
    }

    private void OnRightUp(object sender, MouseButtonEventArgs e)
    {
        var index = IndexAt(e.GetPosition(_canvas));
        if (index < 0)
        {
            if (_flyout is not null) HideFlyout(); else CloseMenu();
            return;
        }

        SetHover(index);
        ShowFlyout(index);
    }

    private static bool IsInside(FrameworkElement element, MouseEventArgs e)
    {
        var p = e.GetPosition(element);
        return p.X >= 0 && p.Y >= 0 && p.X <= element.ActualWidth && p.Y <= element.ActualHeight;
    }

    // ----------------------------------------------------------------- Chức năng (chuột phải)

    private void ShowFlyout(int index)
    {
        HideFlyout();
        var item = _items[index];
        if (item.Actions.Count == 0)
        {
            // Công cụ không có danh sách chức năng: bấm chuột phải cũng chỉ mở công cụ.
            Choose(item, null);
            return;
        }

        var panel = new StackPanel { Margin = new Thickness(6) };
        KeyboardNavigation.SetDirectionalNavigation(panel, KeyboardNavigationMode.Cycle);
        panel.Children.Add(new TextBlock
        {
            Text = Loc.T(item.TitleKey),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 4, 8, 6),
            Foreground = Res("TextPrimary")
        });

        foreach (var action in item.Actions)
        {
            var captured = action;
            var button = new Button
            {
                Content = new TextBlock
                {
                    Text = action.RawLabel ?? Loc.T(action.LabelKey!),
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Left,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                },
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 1, 0, 1),
                Style = (Style)Application.Current.FindResource("SubtleButtonStyle"),
                Foreground = Res("TextPrimary")
            };
            button.Click += (_, _) => Choose(item, captured);
            panel.Children.Add(button);
        }

        var angle = RadialLayout.CenterAngle(index, _items.Count);
        var onRight = angle < 180;

        // Gần mép màn hình: danh sách chức năng chuyển sang phía còn chỗ (cửa sổ đối xứng nên phía kia vẫn nằm trong cửa sổ).
        var area = SystemParameters.WorkArea;
        var rightX = _cx + Outer + Pad;
        var leftX = _cx - Outer - Pad - FlyoutWidth;
        bool FitsRight() => Left + rightX + FlyoutWidth <= area.Right;
        bool FitsLeft() => Left + leftX >= area.Left;
        if (onRight && !FitsRight() && FitsLeft()) onRight = false;
        else if (!onRight && !FitsLeft() && FitsRight()) onRight = true;
        _flyout = new Border
        {
            Child = panel,
            Width = FlyoutWidth,
            Style = (Style)Application.Current.FindResource("CardStyle"),
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.4, Color = Colors.Black }
        };
        _flyout.Measure(new Size(FlyoutWidth, double.PositiveInfinity));
        var (_, wy) = RadialLayout.Point(_cx, _cy, (Inner + Outer) / 2, angle);
        var top = Math.Clamp(wy - _flyout.DesiredSize.Height / 2, 4, Height - _flyout.DesiredSize.Height - 4);
        Canvas.SetLeft(_flyout, onRight ? _cx + Outer + Pad : _cx - Outer - Pad - FlyoutWidth);
        Canvas.SetTop(_flyout, top);
        Panel.SetZIndex(_flyout, 10);
        _canvas.Children.Add(_flyout);

        _hint.Text = Loc.T("RadialHintFlyout");
        if (panel.Children.Count > 1) ((Button)panel.Children[1]).Focus();
    }

    private void HideFlyout()
    {
        if (_flyout is null) return;
        _canvas.Children.Remove(_flyout);
        _flyout = null;
        _hint.Text = Loc.T("RadialHint");
    }

    // ----------------------------------------------------------------- Bàn phím

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_flyout is not null) HideFlyout(); else CloseMenu();
            e.Handled = true;
            return;
        }

        if (_flyout is not null) return; // trong danh sách chức năng: mũi tên/Enter xử lý bởi nút

        switch (e.Key)
        {
            case Key.Right:
            case Key.Down:
                SetHover(RadialLayout.Step(_hovered, 1, _items.Count));
                e.Handled = true;
                break;
            case Key.Left:
            case Key.Up:
                SetHover(RadialLayout.Step(_hovered, -1, _items.Count));
                e.Handled = true;
                break;
            case Key.Enter:
                if (_hovered >= 0) Choose(_items[_hovered], null);
                e.Handled = true;
                break;
            case Key.Space:
                if (_hovered >= 0) ShowFlyout(_hovered);
                e.Handled = true;
                break;
            default:
                var number = e.Key is >= Key.D1 and <= Key.D9 ? e.Key - Key.D1 : e.Key is >= Key.NumPad1 and <= Key.NumPad9 ? e.Key - Key.NumPad1 : -1;
                if (number >= 0 && number < _items.Count)
                {
                    Choose(_items[number], null);
                    e.Handled = true;
                }
                break;
        }
    }

    // ----------------------------------------------------------------- Kết thúc

    private void Choose(RadialItem item, RadialAction? action)
    {
        CloseMenu();
        _activate(item, action);
    }

    private void CloseMenu()
    {
        if (_closing) return;
        _closing = true;
        try { Close(); } catch (InvalidOperationException) { /* đang đóng */ }
    }

    /// <summary>Hiện menu với tâm tại điểm (toạ độ màn hình vật lý) rồi giữ trong vùng làm việc.</summary>
    public void ShowAt(int screenX, int screenY)
    {
        var dpi = Application.Current.MainWindow is { } main ? VisualTreeHelper.GetDpi(main).DpiScaleX : 1.0;
        var x = screenX / dpi - _cx;
        var y = screenY / dpi - _cy;

        var area = SystemParameters.WorkArea;
        // Chỉ vòng tròn cần nằm trọn trong màn hình; vùng trong suốt hai bên được phép tràn ra ngoài.
        x = Math.Clamp(x, area.Left - (_cx - Outer - Pad), area.Right - (_cx + Outer + Pad));
        y = Math.Clamp(y, area.Top - Pad, area.Bottom - (_cy + Outer + 70));

        Left = x;
        Top = y;
        Show();
        Activate();
        Focus();
    }
}
