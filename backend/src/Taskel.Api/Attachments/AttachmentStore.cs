using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Taskel.Api.Data;
using Taskel.Api.Models;

namespace Taskel.Api.Attachments;

public class FileTooLargeException(long limit) : Exception($"The file is larger than {limit} bytes.")
{
    public long Limit { get; } = limit;
}

/// <summary>
/// Keeps attachment files on disk and knows the size limit. Files are stored under names Taskel
/// generates; the name a file was uploaded with is only ever kept in the database.
/// </summary>
public class AttachmentStore(TaskelDbContext db, IOptions<AttachmentOptions> options, IWebHostEnvironment environment)
{
    public const string MaxSizeSetting = "attachments.maxFileSizeMb";
    private const long Megabyte = 1024 * 1024;

    public int MaxFilesPerTask => Math.Max(1, options.Value.MaxFilesPerTask);

    private string Root
    {
        get
        {
            var path = options.Value.Path;
            return System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(environment.ContentRootPath, path);
        }
    }

    /// <summary>The limit in megabytes: what an administrator set, otherwise the configured default.</summary>
    public async Task<int> GetMaxFileSizeMbAsync(CancellationToken ct = default)
    {
        var saved = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == MaxSizeSetting, ct);
        return saved is not null && int.TryParse(saved.Value, out var mb) && mb is >= 1 and <= AttachmentOptions.MaxConfigurableMb
            ? mb
            : Math.Clamp(options.Value.MaxFileSizeMb, 1, AttachmentOptions.MaxConfigurableMb);
    }

    public async Task<long> GetMaxFileSizeBytesAsync(CancellationToken ct = default) =>
        await GetMaxFileSizeMbAsync(ct) * Megabyte;

    public async Task SetMaxFileSizeMbAsync(int megabytes, CancellationToken ct = default)
    {
        var saved = await db.Settings.FirstOrDefaultAsync(s => s.Key == MaxSizeSetting, ct);
        if (saved is null) db.Settings.Add(new AppSetting { Key = MaxSizeSetting, Value = megabytes.ToString() });
        else saved.Value = megabytes.ToString();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Writes a stream to a new file and returns its stored name and size. Stops, and leaves
    /// nothing behind, as soon as the content passes <paramref name="limit"/> bytes.
    /// </summary>
    /// <exception cref="FileTooLargeException">The content is larger than the limit.</exception>
    public async Task<(string StoredName, long Size)> SaveAsync(Stream content, long limit, CancellationToken ct = default)
    {
        System.IO.Directory.CreateDirectory(Root);
        var storedName = Guid.NewGuid().ToString("N");
        var path = System.IO.Path.Combine(Root, storedName);
        long size = 0;
        try
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            var buffer = new byte[81920];
            int read;
            while ((read = await content.ReadAsync(buffer, ct)) > 0)
            {
                size += read;
                if (size > limit) throw new FileTooLargeException(limit);
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }
        catch
        {
            Delete(storedName);
            throw;
        }
        return (storedName, size);
    }

    /// <summary>Opens a stored file for reading, or returns null when it is no longer on disk.</summary>
    public Stream? Open(string storedName)
    {
        var path = PathOf(storedName);
        return path is not null && File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;
    }

    /// <summary>Removes a stored file. A file that is already gone is not an error.</summary>
    public void Delete(string storedName)
    {
        try
        {
            if (PathOf(storedName) is { } path) File.Delete(path);
        }
        catch (IOException)
        {
            // Left on disk; the database no longer points to it.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Stored names are 32 hex digits. Anything else is refused, so a name can never leave the folder.</summary>
    private string? PathOf(string storedName) =>
        storedName.Length == 32 && storedName.All(Uri.IsHexDigit) ? System.IO.Path.Combine(Root, storedName) : null;

    /// <summary>
    /// The name to show for an uploaded file: its last path segment, without control characters,
    /// at most 255 characters. Never used as a path.
    /// </summary>
    public static string CleanFileName(string? raw)
    {
        var name = (raw ?? string.Empty).Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        name = Validation.SafeText.Clean(name, multiline: false).Trim().Trim('.').Trim();
        if (name.Length > 255)
        {
            var extension = System.IO.Path.GetExtension(name);
            if (extension.Length > 20) extension = string.Empty;
            name = name[..(255 - extension.Length)] + extension;
        }
        return name.Length == 0 ? "fisier" : name;
    }
}
