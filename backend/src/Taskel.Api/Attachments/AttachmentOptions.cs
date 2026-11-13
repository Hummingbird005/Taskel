namespace Taskel.Api.Attachments;

/// <summary>Bound from the <c>Attachments</c> configuration section.</summary>
public class AttachmentOptions
{
    public const string Section = "Attachments";
    /// <summary>The largest limit an administrator can set from the interface.</summary>
    public const int MaxConfigurableMb = 2048;

    /// <summary>
    /// Largest file accepted, in megabytes, until an administrator sets another value from the
    /// interface. That value, once set, wins over this one.
    /// </summary>
    public int MaxFileSizeMb { get; set; } = 100;
    /// <summary>How many files one task can hold.</summary>
    public int MaxFilesPerTask { get; set; } = 20;
    /// <summary>Folder the files are kept in. A relative path is taken from the application's folder.</summary>
    public string Path { get; set; } = "data/attachments";
}
