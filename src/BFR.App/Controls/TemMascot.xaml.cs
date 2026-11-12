namespace BFR.App.Controls;

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using BFR.App.Infrastructure;

/// <summary>Biểu cảm của Tem.</summary>
public enum TemState
{
    /// <summary>Nghỉ: mắt mở, chớp chậm, nhấp nhô nhẹ.</summary>
    Idle,

    /// <summary>Đang làm: mắt liếc theo thanh tiến trình.</summary>
    Working,

    /// <summary>Vui: mắt cong, pháo giấy (khi xong việc).</summary>
    Happy,

    /// <summary>Ái ngại: lông mày xệ, giọt mồ hôi (khi lỗi hoặc xung đột).</summary>
    Oops,

    /// <summary>Ngủ: khi app rảnh lâu.</summary>
    Sleep
}

/// <summary>
/// Linh vật Tem (vector). Đổi <see cref="State"/> để đổi biểu cảm. Mọi chuyển động tự tắt khi người dùng
/// bật giảm chuyển động của Windows hoặc dùng tương phản cao; lúc đó chỉ còn các khung hình tĩnh.
/// </summary>
public partial class TemMascot : UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(TemState), typeof(TemMascot),
        new PropertyMetadata(TemState.Idle, (d, _) => ((TemMascot)d).ApplyState()));

    public TemMascot()
    {
        InitializeComponent();
        System.Windows.Automation.AutomationProperties.SetName(this, Loc.T("TemName"));
        Loaded += (_, _) => { ApplyState(); StartAmbientMotion(); };
        Unloaded += (_, _) => StopAmbientMotion();
    }

    public TemState State
    {
        get => (TemState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    private void Show(UIElement element, bool visible) => element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private void ApplyState()
    {
        if (!IsInitialized) return;

        var s = State;
        Show(EyesOpen, s is TemState.Idle or TemState.Oops);
        Show(EyesHappy, s == TemState.Happy);
        Show(EyesSleep, s == TemState.Sleep);
        Show(EyesWork, s == TemState.Working);

        Show(Brows, s == TemState.Oops);
        Show(Sweat, s == TemState.Oops);
        Show(Zzz, s == TemState.Sleep);
        Show(Progress, s == TemState.Working);
        Show(Confetti, s == TemState.Happy && !BfrTheme.ReduceMotion);

        Show(MouthSmile, s == TemState.Idle);
        Show(MouthBig, s == TemState.Happy);
        Show(MouthFrown, s == TemState.Oops);
        Show(MouthO, s == TemState.Sleep);
        Show(MouthFlat, s == TemState.Working);

        RestartStateMotion();
    }

    /// <summary>Chuyển động theo biểu cảm: thanh tiến trình chạy qua lại, pháo giấy rơi một lần.</summary>
    private void RestartStateMotion()
    {
        ProgressFill.BeginAnimation(WidthProperty, null);
        foreach (var piece in Confetti.Children.OfType<Rectangle>())
        {
            var move = (TranslateTransform)piece.RenderTransform;
            move.BeginAnimation(TranslateTransform.YProperty, null);
            piece.BeginAnimation(OpacityProperty, null);
        }

        if (BfrTheme.ReduceMotion || !IsLoaded) return;

        if (State == TemState.Working)
        {
            ProgressFill.BeginAnimation(WidthProperty, new DoubleAnimation(8, 56, TimeSpan.FromSeconds(1.2))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
        }
        else if (State == TemState.Happy)
        {
            var index = 0;
            foreach (var piece in Confetti.Children.OfType<Rectangle>())
            {
                var delay = TimeSpan.FromMilliseconds(index++ * 150);
                var move = (TranslateTransform)piece.RenderTransform;
                move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-14, 26, TimeSpan.FromSeconds(1.4)) { BeginTime = delay, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
                piece.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1.4)) { BeginTime = delay });
            }
        }
    }

    /// <summary>Chuyển động nền luôn có: nhấp nhô nhẹ và chớp mắt chậm.</summary>
    private void StartAmbientMotion()
    {
        StopAmbientMotion();
        if (BfrTheme.ReduceMotion) return;

        Bob.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -3, TimeSpan.FromSeconds(1.6))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });

        // Chớp mắt: mở gần hết chu kỳ, khép nhanh ở cuối.
        var blink = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(4.2), RepeatBehavior = RepeatBehavior.Forever };
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(0)));
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.92)));
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(0.1, KeyTime.FromPercent(0.95)));
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.98)));
        Blink.BeginAnimation(ScaleTransform.ScaleYProperty, blink);

        RestartStateMotion();
    }

    private void StopAmbientMotion()
    {
        Bob.BeginAnimation(TranslateTransform.YProperty, null);
        Blink.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }
}
