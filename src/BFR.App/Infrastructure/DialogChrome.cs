namespace BFR.App.Infrastructure;

using System.Windows;
using System.Windows.Controls;
using Ui = Wpf.Ui.Controls;

/// <summary>
/// Khung chung cho hộp thoại dựng bằng code: FluentWindow + Mica + TitleBar của WPF-UI.
/// Gọi <see cref="Apply"/> trong constructor rồi gán <c>Content = DialogChrome.Wrap(this, body)</c>.
/// </summary>
public static class DialogChrome
{
    public static void Apply(Ui.FluentWindow window)
    {
        window.ExtendsContentIntoTitleBar = true;
        window.WindowBackdropType = Ui.WindowBackdropType.Mica;
    }

    /// <summary>Ánh xạ tên style cũ của BFR sang kiểu nút của WPF-UI.</summary>
    public static Ui.ControlAppearance AppearanceFor(string styleKey) => styleKey switch
    {
        var k when k.StartsWith("Primary") => Ui.ControlAppearance.Primary,
        var k when k.StartsWith("Subtle") => Ui.ControlAppearance.Transparent,
        _ => Ui.ControlAppearance.Secondary
    };

    /// <summary>Đặt <paramref name="body"/> dưới một TitleBar (chỉ có nút đóng).</summary>
    public static Grid Wrap(Ui.FluentWindow window, UIElement body)
    {
        var shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var titleBar = new Ui.TitleBar { Title = window.Title, ShowMinimize = false, ShowMaximize = false };
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(body, 1);
        shell.Children.Add(titleBar);
        shell.Children.Add(body);
        return shell;
    }
}
