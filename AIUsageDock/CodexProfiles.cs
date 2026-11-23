namespace AIUsageDock;

internal static class CodexProfiles
{
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex-usage-dock");
    public static string Home(int slot, string service = "codex")
    {
        if (slot < 1) throw new ArgumentOutOfRangeException(nameof(slot));
        if (service is not ("codex" or "claude")) throw new ArgumentException("Unknown service");
        // AppData writes from MSIX are redirected to LocalCache. The native CLI
        // does not see that redirected path. Use a dedicated user-profile folder
        // shared by both processes, outside the AppData virtualization boundary.
        return Path.Combine(Root, "profiles", service == "claude" ? $"claude-account-{slot}" : $"account-{slot}");
    }
    public static void Delete(int slot, string service = "codex")
        => DeleteUnderRoot(Root, slot, service);
    internal static void DeleteUnderRoot(string storageRoot, int slot, string service = "codex")
    {
        if (slot < 1) throw new ArgumentOutOfRangeException(nameof(slot));
        if (service is not ("codex" or "claude")) throw new ArgumentException("Unknown service");
        var root = Path.GetFullPath(Path.Combine(storageRoot, "profiles")) + Path.DirectorySeparatorChar;
        var directory = Path.GetFullPath(Path.Combine(root, service == "claude" ? $"claude-account-{slot}" : $"account-{slot}"));
        if (!directory.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid profile path");
        if (!Directory.Exists(directory)) return;
        foreach (var parent in new[] { storageRoot, root })
            if (File.GetAttributes(parent).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Profile root is a link");
        RejectLinks(new DirectoryInfo(directory));
        Directory.Delete(directory, recursive: true);
    }
    private static void RejectLinks(DirectoryInfo directory)
    {
        if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Profile contains a directory link");
        foreach (var item in directory.EnumerateFileSystemInfos())
        {
            if (item.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Profile contains a link");
            if (item is DirectoryInfo child) RejectLinks(child);
        }
    }
}
