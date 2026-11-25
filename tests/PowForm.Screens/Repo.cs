namespace PowForm.Screens;

/// <summary>Where things are: the repository root and the build configuration of this test build.</summary>
internal static class Repo
{
    public static string Root { get; } = FindRoot();

    /// <summary>"debug" or "release": the folder name under artifacts/bin of this test build.</summary>
    public static string Configuration { get; } = new DirectoryInfo(AppContext.BaseDirectory).Name;

    /// <summary>
    /// Where screenshots (and later measurements) are written: artifacts/ (ignored by git), or the
    /// folder in POW_OUTPUT.
    /// </summary>
    public static string Output { get; } = Environment.GetEnvironmentVariable("POW_OUTPUT") is { Length: > 0 } folder
        ? Path.GetFullPath(folder, Root)
        : Path.Combine(Root, "artifacts");

    public static string Shot(string name)
    {
        var folder = Path.Combine(Output, "screenshots");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, name);
    }

    private static string FindRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "PowForm.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("PowForm.slnx not found above the test build.");
    }
}
