using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PixFix.Core.Common;
using PixFix.Infrastructure.Data;

namespace PixFix.Infrastructure.Assets;

/// <summary>
/// Inhaltsadressierte Dateiablage unter <c>assets/&lt;2 Zeichen&gt;/&lt;sha256&gt;.&lt;endung&gt;</c>.
/// Identische Dateien werden nur einmal gespeichert; vorhandene Dateien werden nie überschrieben.
/// </summary>
public sealed class AssetStore
{
    private static readonly Dictionary<string, string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
        [".webp"] = "image/webp",
        [".pdf"] = "application/pdf",
        [".rtf"] = "application/rtf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".txt"] = "text/plain",
    };

    private readonly AppPaths paths;

    public AssetStore(AppPaths paths)
    {
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public string GetFullPath(Asset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return GetFullPath(asset.RelativePath);
    }

    public string GetFullPath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return Path.Combine(paths.DataDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// Kopiert eine Datei in die Ablage und registriert sie in der Tabelle <c>assets</c>.
    /// Die Quelldatei wird nur gelesen.
    /// </summary>
    public Asset Import(string sourceFile, SqliteConnection connection, SqliteTransaction? transaction, DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);
        ArgumentNullException.ThrowIfNull(connection);

        var hash = ComputeSha256(sourceFile);
        var extension = Path.GetExtension(sourceFile).ToLowerInvariant();
        var relativePath = "assets/" + hash[..2] + "/" + hash + extension;
        var target = Path.Combine(paths.DataDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(target))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var temp = target + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(sourceFile, temp);
                if (!string.Equals(ComputeSha256(temp), hash, StringComparison.Ordinal))
                {
                    throw new IOException($"Kopie von '{sourceFile}' ist fehlerhaft (Prüfsumme weicht ab).");
                }

                File.Move(temp, target);
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
        }

        var asset = new Asset(hash, relativePath, MediaTypeFor(extension), new FileInfo(target).Length, Path.GetFileName(sourceFile));
        connection.Execute(
            """
            INSERT INTO assets(id, relative_path, media_type, length, original_name, created_utc)
            VALUES($id, $path, $type, $length, $name, $created)
            ON CONFLICT(id) DO NOTHING;
            """,
            transaction,
            ("$id", asset.Id),
            ("$path", asset.RelativePath),
            ("$type", asset.MediaType),
            ("$length", asset.Length),
            ("$name", asset.OriginalName),
            ("$created", nowUtc.ToDb()));

        // Bei bereits vorhandenem Asset gilt der gespeicherte Pfad.
        var storedPath = connection.Scalar("SELECT relative_path FROM assets WHERE id = $id;", transaction, ("$id", asset.Id)) as string;
        return storedPath is null || storedPath == asset.RelativePath ? asset : asset with { RelativePath = storedPath };
    }

    public static string MediaTypeFor(string extension) =>
        MediaTypes.TryGetValue(extension, out var type) ? type : "application/octet-stream";

    public static string ComputeSha256(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
