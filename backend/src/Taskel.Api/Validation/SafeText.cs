using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Taskel.Api.Validation;

/// <summary>
/// Rejects text the application cannot store or show safely: control characters (including NUL,
/// which PostgreSQL refuses) and unpaired UTF-16 surrogates. Works on a string or a list of strings.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class SafeTextAttribute : ValidationAttribute
{
    /// <summary>Allow line breaks and tabs, for free text such as notes.</summary>
    public bool Multiline { get; set; }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var safe = value switch
        {
            null => true,
            string text => SafeText.IsSafe(text, Multiline),
            IEnumerable<string?> items => items.All(item => item is null || SafeText.IsSafe(item, Multiline)),
            _ => true
        };
        return safe
            ? ValidationResult.Success
            : new ValidationResult($"{validationContext.DisplayName} contains characters that are not allowed.");
    }
}

public static class SafeText
{
    public static bool IsSafe(string text, bool multiline)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (!IsSafeAt(text, ref i, multiline)) return false;
        }
        return true;
    }

    /// <summary>Removes the characters <see cref="IsSafe"/> rejects. For content we must accept, such as emails.</summary>
    public static string Clean(string? text, bool multiline)
    {
        if (string.IsNullOrEmpty(text) || IsSafe(text, multiline)) return text ?? string.Empty;

        var cleaned = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var start = i;
            if (IsSafeAt(text, ref i, multiline)) cleaned.Append(text, start, i - start + 1);
        }
        return cleaned.ToString();
    }

    /// <summary>Checks the character at <paramref name="i"/>, advancing over the low half of a surrogate pair.</summary>
    private static bool IsSafeAt(string text, ref int i, bool multiline)
    {
        var c = text[i];
        if (char.IsHighSurrogate(c))
        {
            if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
                return true;
            }
            return false;
        }
        if (char.IsLowSurrogate(c)) return false;
        if (char.IsControl(c)) return multiline && c is '\n' or '\r' or '\t';
        return true;
    }
}
