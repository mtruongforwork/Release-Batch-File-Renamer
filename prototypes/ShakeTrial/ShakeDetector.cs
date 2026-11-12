namespace ShakeTrial;

/// <summary>
/// Nhận diện "lắc ngang" từ chuỗi điểm chuột (x, thời gian ms).
/// Lắc = đủ số lần đổi chiều ngang trong cửa sổ thời gian, mỗi nhịp lắc đủ biên độ.
/// Logic thuần, không phụ thuộc Win32 nên test được.
/// </summary>
public sealed class ShakeDetector
{
    private readonly int _windowMs;
    private readonly int _minSwingPx;
    private readonly int _reversals;
    private readonly int _cooldownMs;

    private readonly List<(int X, long T)> _samples = new();
    private long _lastTriggerT = long.MinValue / 2;

    public ShakeDetector(int windowMs = 700, int minSwingPx = 40, int reversals = 3, int cooldownMs = 1000)
    {
        _windowMs = windowMs;
        _minSwingPx = minSwingPx;
        _reversals = reversals;
        _cooldownMs = cooldownMs;
    }

    public void Reset() => _samples.Clear();

    /// <summary>Đưa một mẫu vào; trả true đúng lúc phát hiện lắc.</summary>
    public bool Feed(int x, long tMs)
    {
        _samples.Add((x, tMs));
        _samples.RemoveAll(s => tMs - s.T > _windowMs);

        if (tMs - _lastTriggerT < _cooldownMs) return false;
        if (CountReversals() < _reversals) return false;

        _lastTriggerT = tMs;
        _samples.Clear();
        return true;
    }

    private int CountReversals()
    {
        // Đếm điểm đảo chiều: mỗi lần quãng đường ngược chiều so với cực trị trước ≥ _minSwingPx.
        int count = 0, dir = 0, extreme = _samples[0].X;
        foreach (var (x, _) in _samples)
        {
            if (dir >= 0 && x - extreme <= -_minSwingPx)
            {
                if (dir != 0) count++;
                dir = -1; extreme = x;
            }
            else if (dir <= 0 && x - extreme >= _minSwingPx)
            {
                if (dir != 0) count++;
                dir = 1; extreme = x;
            }
            else if ((dir == 1 && x > extreme) || (dir == -1 && x < extreme))
            {
                extreme = x;
            }
        }
        return count;
    }
}
