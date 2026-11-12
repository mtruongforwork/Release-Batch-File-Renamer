namespace BFR.App.Infrastructure;

using System;
using System.Windows;
using System.Windows.Markup;

/// <summary>Cỡ thời lượng chuyển động trong bộ token (spec giao diện, mục 3.3).</summary>
public enum MotionSpeed
{
    Fast,
    Base,
    Slow
}

/// <summary>
/// <c>{infra:Dur Fast}</c>: thời lượng chuyển động lấy theo chế độ giảm chuyển động tại thời điểm nạp XAML (0 khi giảm).
/// Dùng thay cho DynamicResource vì Storyboard trong Style phải đóng băng được.
/// </summary>
[MarkupExtensionReturnType(typeof(Duration))]
public sealed class DurExtension : MarkupExtension
{
    public DurExtension()
    {
    }

    public DurExtension(MotionSpeed speed)
    {
        Speed = speed;
    }

    public MotionSpeed Speed { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (BfrTheme.ReduceMotion) return new Duration(TimeSpan.Zero);

        return new Duration(TimeSpan.FromMilliseconds(Speed switch
        {
            MotionSpeed.Fast => 90,
            MotionSpeed.Base => 220,
            _ => 600
        }));
    }
}
