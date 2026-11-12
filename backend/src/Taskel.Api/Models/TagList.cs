using System.ComponentModel.DataAnnotations;
using Taskel.Api.Validation;

namespace Taskel.Api.Models;

/// <summary>Validates a list of tags and provides the normalization applied before storing them.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class TagListAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null) return ValidationResult.Success;
        if (value is not IEnumerable<string?> tags) return new ValidationResult("Tags must be a list of strings.");

        var normalized = TagList.Normalize(tags);
        if (normalized.Any(t => !SafeText.IsSafe(t, multiline: false) || t.Contains(',')))
            return new ValidationResult("Tags cannot contain commas or control characters.");
        if (normalized.Count > TagList.MaxTags)
            return new ValidationResult($"A task can have at most {TagList.MaxTags} tags.");
        if (normalized.Any(t => t.Length > TagList.MaxTagLength))
            return new ValidationResult($"A tag can be at most {TagList.MaxTagLength} characters long.");
        return ValidationResult.Success;
    }
}

public static class TagList
{
    public const int MaxTags = 10;
    public const int MaxTagLength = 30;

    /// <summary>Trims, lower-cases, drops blanks and duplicates; keeps the first-seen order.</summary>
    public static List<string> Normalize(IEnumerable<string?>? tags) =>
        (tags ?? [])
            .Select(t => t?.Trim().ToLowerInvariant() ?? string.Empty)
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();
}
