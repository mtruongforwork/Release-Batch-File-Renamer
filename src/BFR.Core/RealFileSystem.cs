namespace BFR.Core;

using System.IO;

/// <summary>
/// Triển khai IFileSystem thật trên Windows/.NET.
/// </summary>
public sealed class RealFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public void EnsureDirectory(string path) => Directory.CreateDirectory(path);
    public void Move(string from, string to, bool overwrite) => File.Move(from, to, overwrite);
    public string[] GetFiles(string directory, string pattern, bool recursive) =>
        Directory.Exists(directory)
            ? Directory.GetFiles(directory, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            : Array.Empty<string>();

    public string[] GetDirectories(string directory, bool recursive) =>
        Directory.Exists(directory)
            ? Directory.GetDirectories(directory, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            : Array.Empty<string>();
    public string ReadAllText(string path) => File.ReadAllText(path);
    public void WriteAllText(string path, string content) => File.WriteAllText(path, content);
    public void AppendAllText(string path, string content) => File.AppendAllText(path, content);
    public void Delete(string path, bool recursive)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive);
        else if (File.Exists(path)) File.Delete(path);
        else throw new FileNotFoundException("Tệp không tồn tại", path);
    }
    public long GetLength(string path) => new FileInfo(path).Length;
    public DateTime GetCreationTimeUtc(string path) => File.GetCreationTimeUtc(path);
    public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);

    public void Copy(string from, string to, bool overwrite) => File.Copy(from, to, overwrite);

    public FileTimes GetTimes(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Tệp không tồn tại", path);
        return FileTimes.From(info.CreationTimeUtc, info.LastWriteTimeUtc, info.LastAccessTimeUtc);
    }

    public void SetTimes(string path, FileTimes times)
    {
        if (Directory.Exists(path))
        {
            Directory.SetCreationTimeUtc(path, times.CreatedUtc);
            Directory.SetLastWriteTimeUtc(path, times.ModifiedUtc);
            Directory.SetLastAccessTimeUtc(path, times.AccessedUtc);
        }
        else
        {
            File.SetCreationTimeUtc(path, times.CreatedUtc);
            File.SetLastWriteTimeUtc(path, times.ModifiedUtc);
            File.SetLastAccessTimeUtc(path, times.AccessedUtc);
        }
    }

    public FileAttributes GetAttributes(string path) => File.GetAttributes(path);

    public void SetAttributes(string path, FileAttributes attributes) => File.SetAttributes(path, attributes);

    public bool IsDirectoryEmpty(string path) =>
        Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any();

    public void WriteAllBytes(string path, byte[] content)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        File.WriteAllBytes(path, content);
    }

    public Stream OpenWrite(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920);
    }

    public Stream OpenRead(string path) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.SequentialScan);

    public bool IsLink(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            return (attrs & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true; // không đọc được thì coi như không an toàn để đi vào
        }
    }
}
