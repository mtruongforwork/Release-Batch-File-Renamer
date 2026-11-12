namespace BFR.Core;

using System;

/// <summary>
/// Kết quả phân tích dòng lệnh khởi động của ứng dụng chính.
/// <c>--background</c> được tách khỏi danh sách đường dẫn để không bị coi là tệp.
/// </summary>
public sealed record StartupArgs(IpcPayload Payload, bool Background)
{
    public static StartupArgs Parse(string[] args)
    {
        var payload = new IpcPayload();
        var background = false;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (string.Equals(a, "--file-list", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) payload.FileListPath = args[++i];
            }
            else if (string.Equals(a, "--preset", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) payload.PresetName = args[++i];
            }
            else if (string.Equals(a, "--quick", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) payload.QuickPreset = args[++i];
            }
            else if (string.Equals(a, "--tool", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) payload.Tool = args[++i];
            }
            else if (string.Equals(a, "--background", StringComparison.OrdinalIgnoreCase))
            {
                background = true;
            }
            else
            {
                payload.Paths.Add(a);
            }
        }

        return new StartupArgs(payload, background);
    }
}
