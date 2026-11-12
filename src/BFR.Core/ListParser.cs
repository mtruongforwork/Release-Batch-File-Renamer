namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.Text;

/// <summary>Một dòng của danh sách: cột 1, và cột 2 nếu có (null khi danh sách chỉ có một cột).</summary>
public sealed record ListRow(string First, string? Second);

/// <summary>
/// Đọc danh sách dán từ Excel / CSV: mỗi dòng "cũ ⇥ mới". Tự nhận dấu phân cách
/// (tab, rồi ';', rồi ','), hỗ trợ trường đặt trong dấu ngoặc kép, bỏ dòng trống và BOM.
/// </summary>
public static class ListParser
{
    /// <summary>Dấu phân cách được nhận dạng từ dòng đầu tiên có nội dung; NUL nếu chỉ một cột.</summary>
    public static char DetectDelimiter(string? text)
    {
        if (string.IsNullOrEmpty(text)) return '\0';
        foreach (var line in SplitLines(text.TrimStart('﻿')))
        {
            if (line.Trim().Length == 0) continue;
            if (line.Contains('\t')) return '\t';
            if (line.Contains(';')) return ';';
            if (line.Contains(',')) return ',';
            return '\0';
        }
        return '\0';
    }

    /// <summary>Phân tích văn bản thành các dòng; trả về tối đa <paramref name="maxRows"/> dòng.</summary>
    public static IReadOnlyList<ListRow> Parse(string? text, int maxRows = int.MaxValue)
    {
        var rows = new List<ListRow>();
        if (string.IsNullOrEmpty(text) || maxRows <= 0) return rows;

        text = text.TrimStart('﻿');
        var delimiter = DetectDelimiter(text);

        foreach (var line in SplitLines(text))
        {
            if (line.Trim().Length == 0) continue;
            if (rows.Count >= maxRows) break;

            if (delimiter == '\0')
            {
                rows.Add(new ListRow(Unquote(line.Trim()), null));
                continue;
            }

            var fields = SplitFields(line, delimiter);
            // Trường đặt trong dấu ngoặc kép giữ nguyên khoảng trắng (ví dụ "copy of "); trường thường được cắt.
            var first = fields[0].Quoted ? fields[0].Text : fields[0].Text.Trim();
            string? second = fields.Count > 1 ? (fields[1].Quoted ? fields[1].Text : fields[1].Text.Trim()) : null;
            if (first.Length == 0 && string.IsNullOrEmpty(second)) continue;
            rows.Add(new ListRow(first, second));
        }

        return rows;
    }

    /// <summary>Đếm số dòng có nội dung (không giới hạn), để so với hạn mức Free.</summary>
    public static int CountRows(string? text) => Parse(text).Count;

    private static IEnumerable<string> SplitLines(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n' || text[i] == '\r')
            {
                yield return text.Substring(start, i - start);
                if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                start = i + 1;
            }
        }
        if (start < text.Length) yield return text.Substring(start);
    }

    private readonly record struct Field(string Text, bool Quoted);

    private static List<Field> SplitFields(string line, char delimiter)
    {
        var fields = new List<Field>();
        var sb = new StringBuilder();
        var inQuotes = false;
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else if (c == '"' && sb.ToString().Trim().Length == 0) { inQuotes = true; quoted = true; sb.Clear(); }
            else if (c == delimiter) { fields.Add(new Field(sb.ToString(), quoted)); sb.Clear(); quoted = false; }
            else sb.Append(c);
        }

        fields.Add(new Field(sb.ToString(), quoted));
        return fields;
    }

    private static string Unquote(string s) =>
        s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1].Replace("\"\"", "\"") : s;
}
