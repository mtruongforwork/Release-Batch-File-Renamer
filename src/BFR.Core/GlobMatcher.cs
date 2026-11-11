namespace BFR.Core;

using System;

/// <summary>
/// So khớp glob đơn giản (<c>*</c> và <c>?</c>), không phân biệt hoa/thường.
/// Dùng thuật toán lặp O(n·m) thay vì dựng Regex, nên mẫu như <c>*a*a*a*b</c> không gây backtracking.
/// </summary>
public static class GlobMatcher
{
    public static bool IsMatch(string name, string glob)
    {
        if (glob is null) return false;
        name ??= string.Empty;

        int n = 0, g = 0, star = -1, mark = 0;
        while (n < name.Length)
        {
            if (g < glob.Length && glob[g] != '*' &&
                (glob[g] == '?' || char.ToUpperInvariant(glob[g]) == char.ToUpperInvariant(name[n])))
            {
                n++;
                g++;
            }
            else if (g < glob.Length && glob[g] == '*')
            {
                star = g++;
                mark = n;
            }
            else if (star >= 0)
            {
                g = star + 1;
                n = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (g < glob.Length && glob[g] == '*') g++;
        return g == glob.Length;
    }
}
