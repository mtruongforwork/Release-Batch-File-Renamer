namespace BFR.Core;

using System;

/// <summary>
/// Giới hạn thời gian thống nhất cho mọi Regex trong luồng đổi tên (chống ReDoS).
/// Regex Tester tương tác dùng giới hạn riêng dài hơn (<see cref="RegexProbe.TimeoutMilliseconds"/>).
/// </summary>
public static class RegexLimits
{
    public const int TimeoutMilliseconds = 250;

    public static TimeSpan Timeout { get; } = TimeSpan.FromMilliseconds(TimeoutMilliseconds);
}
