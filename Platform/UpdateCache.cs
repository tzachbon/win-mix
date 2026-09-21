using System.Text.RegularExpressions;

namespace Mix.Platform;

sealed class UpdateCache(string directory)
{
    readonly string directory = Path.GetFullPath(directory);
    static readonly Regex OwnedName = new(@"\A[0-9a-f]{32}\.win-mix-update\.(exe|partial)\z", RegexOptions.CultureInvariant);

    public string CreatePartial()
    {
        CheckDirectory();
        Directory.CreateDirectory(directory);
        CheckDirectory();
        return Path.Combine(directory, $"{Guid.NewGuid():N}.win-mix-update.partial");
    }

    void CheckDirectory()
    {
        // Refuse junctions in the cache and its app-owned parent.
        foreach (var path in new[] { directory, Path.GetDirectoryName(directory)! })
            if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The update cache is redirected. Open the release page to update manually.");
    }

    public void Remove(string path)
    {
        try
        {
            CheckDirectory();
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), directory, StringComparison.OrdinalIgnoreCase) ||
                !OwnedName.IsMatch(Path.GetFileName(path))) return;
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) File.Delete(path);
        }
        catch (IOException) { } // An installer can still have its source file open.
        catch (UnauthorizedAccessException) { }
    }

    public void Cleanup()
    {
        try
        {
            CheckDirectory();
            if (!Directory.Exists(directory)) return;
            foreach (var file in Directory.EnumerateFiles(directory)) Remove(file);
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
