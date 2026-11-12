using System.IO.Compression;

namespace StreamDeckIconManager.Mac;

/// <summary>
/// Reads and writes shareable icon pack files.
/// <list type="bullet">
/// <item><c>.streamDeckIconPack</c>: Stream Deck's own format, a zip holding one
/// <c>&lt;id&gt;.sdIconPack</c> folder. Opening one installs it in Stream Deck, live, with no restart.</item>
/// <item><c>.zip</c>: exports from version 1.1 and earlier. Same folder inside, plus the app's backup folder.</item>
/// </list>
/// </summary>
internal static class IconPackFile
{
    public const string Extension = ".streamDeckIconPack";

    public static bool IsImportable(string path) =>
        path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Writes the pack as a .streamDeckIconPack, from what's in memory (so unsaved changes are
    /// included) and without the app's backup folder. Returns the manifest Version it carries.
    /// </summary>
    public static string Export(Manifest pack, string outputPath)
    {
        var work = Path.Combine(Path.GetTempPath(), "sdim-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Keep the pack's own folder name: it's the pack's ID in Stream Deck, so installing a newer
            // export of the same pack replaces the old one instead of adding a second copy.
            var folderName = Path.GetFileName(pack.IconPackPath.TrimEnd(Path.DirectorySeparatorChar));
            if (!folderName.EndsWith(".sdIconPack", StringComparison.OrdinalIgnoreCase))
                folderName = PackPaths.BuildPackFolderName(pack.Name);

            var packFolder = Path.Combine(work, folderName);
            pack.WriteTo(packFolder);

            if (File.Exists(outputPath))
                File.Delete(outputPath);
            ZipFile.CreateFromDirectory(packFolder, outputPath, CompressionLevel.Optimal, includeBaseDirectory: true);

            using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(packFolder, "manifest.json")));
            return manifest.RootElement.GetProperty("Version").GetString() ?? "";
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { /* temp cleanup is best-effort */ }
        }
    }

    /// <summary>
    /// Unpacks an icon pack file into <paramref name="workDir"/> and returns the pack folder,
    /// or null if the file doesn't contain one.
    /// </summary>
    public static string? Extract(string path, string workDir)
    {
        ZipFile.ExtractToDirectory(path, workDir, overwriteFiles: true);

        // Usually "<id>.sdIconPack/" at the top level; also accept a pack zipped without its folder.
        // Skip the "__MACOSX" folder that zips made with Finder include.
        var folders = Directory.GetDirectories(workDir, "*", SearchOption.AllDirectories)
            .Where(d => !d.Contains("__MACOSX", StringComparison.Ordinal))
            .ToList();
        var folder = folders.FirstOrDefault(d => d.EndsWith(".sdIconPack", StringComparison.OrdinalIgnoreCase)
                                                 && File.Exists(Path.Combine(d, "manifest.json")))
                     ?? folders.FirstOrDefault(d => d.Contains(".sdIconPack", StringComparison.OrdinalIgnoreCase));

        if (folder is null && File.Exists(Path.Combine(workDir, "manifest.json")))
            folder = workDir;

        return folder;
    }
}
