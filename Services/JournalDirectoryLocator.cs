using System.IO;
using System.Runtime.InteropServices;

namespace ColonyTracker.Services;

/// <summary>
/// Resolves the "Saved Games\Frontier Developments\Elite Dangerous" journal directory via the
/// native Known Folder API, since Saved Games is a user-relocatable known folder and isn't
/// exposed through Environment.SpecialFolder.
/// </summary>
public static class JournalDirectoryLocator
{
    private static readonly Guid FolderIdSavedGames = new("4C5C32FF-BB9D-43b0-B5B4-2D72E54EAAA4");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr pszPath);

    public static string Resolve(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;

        return Path.Combine(ResolveSavedGamesDir(), "Frontier Developments", "Elite Dangerous");
    }

    private static string ResolveSavedGamesDir()
    {
        try
        {
            if (SHGetKnownFolderPath(FolderIdSavedGames, 0, IntPtr.Zero, out var ptr) == 0 && ptr != IntPtr.Zero)
            {
                try
                {
                    var path = Marshal.PtrToStringUni(ptr);
                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                        return path;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(ptr);
                }
            }
        }
        catch
        {
            // fall through to default below
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games");
    }
}
