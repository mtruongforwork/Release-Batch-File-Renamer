namespace ShakeTrial;

using System.Windows;
using System.Windows.Threading;

public partial class MainWindow : Window
{
    private readonly MouseHook _hook;
    private int _shakes;

    public MainWindow()
    {
        InitializeComponent();
        _hook = new MouseHook(new ShakeDetector());
        _hook.Shaken += (x, y) => Dispatcher.BeginInvoke(() =>
        {
            _shakes++;
            Status.Text = $"LẮC #{_shakes} tại ({x},{y})";
        });

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) => Info.Text = $"Hook sống · nút trái: {(_hook.LeftDown ? "giữ" : "nhả")} · moves: {_hook.Moves}";
        timer.Start();
        Closed += (_, _) => _hook.Dispose();
    }
}
