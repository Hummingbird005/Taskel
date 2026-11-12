using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Taskel.Api.Validation;

/// <summary>A plain address of the form <c>name@host.tld</c>: no display name, spaces or control characters.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed partial class StrictEmailAttribute : ValidationAttribute
{
    public const int MaxLength = 254;

    public static bool IsValidAddress(string? value) =>
        value is not null
        && value.Length <= MaxLength
        && SafeText.IsSafe(value, multiline: false)
        && Address().IsMatch(value);

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) =>
        value is null || (value is string text && IsValidAddress(text.Trim()))
            ? ValidationResult.Success
            : new ValidationResult("Enter a valid email address.");

    // Local part, then dot-separated host labels (no empty label), ending in a label of 2+ characters.
    [GeneratedRegex(@"^[^\s<>@,;:""()\[\]\\]+@(?:[^\s<>@,;:""()\[\]\\.]+\.)+[^\s<>@,;:""()\[\]\\.]{2,}$")]
    private static partial Regex Address();
}
