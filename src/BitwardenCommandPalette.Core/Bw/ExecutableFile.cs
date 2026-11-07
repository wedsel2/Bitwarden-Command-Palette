namespace BitwardenCommandPalette.Core.Bw;

/// <summary>Turns a path into the file it really leads to, for checking and starting.</summary>
internal static class ExecutableFile
{
    /// <summary>
    /// The existing file a fully qualified path leads to, following links to their final
    /// target (winget's Links folder holds one). The result is what gets verified and run, so a
    /// link that is re-pointed later is checked again under its new target.
    /// </summary>
    /// <param name="path">The path to resolve.</param>
    /// <returns>The final target's full path, or null if it is relative, missing or unreadable.</returns>
    public static string? Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return null;
        }

        try
        {
            var file = new FileInfo(path);
            var target = file.LinkTarget is null ? file : file.ResolveLinkTarget(returnFinalTarget: true) as FileInfo;
            return target is { Exists: true } ? target.FullName : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
