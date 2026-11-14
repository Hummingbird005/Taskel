using System.ComponentModel.DataAnnotations;
using Taskel.Api.Integrations;

namespace Taskel.Api.Validation;

/// <summary>A server name or IPv4 address (letters, digits, dots, hyphens), or an IPv6 address in brackets. No port.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class HostNameAttribute : ValidationAttribute
{
    public const int MaxLength = 253;

    public static bool IsValid(string text)
    {
        if (text.Length is 0 or > MaxLength) return false;
        if (text.StartsWith('[') && text.EndsWith(']'))
            return text.Length > 2 && text[1..^1].All(c => char.IsAsciiHexDigit(c) || c is ':' or '.');
        return text.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-');
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) =>
        value is null || (value is string text && (text.Length == 0 || IsValid(text.Trim())))
            ? ValidationResult.Success
            : new ValidationResult($"{validationContext.DisplayName} must be a server name, without http:// or a port.");
}

/// <summary>An absolute http or https address without query, fragment or credentials.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class HttpUrlAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) =>
        value is null || (value is string text && (text.Length == 0 || PublicAddress.Parse(text) is not null))
            ? ValidationResult.Success
            : new ValidationResult($"{validationContext.DisplayName} must be an address starting with https:// or http://.");
}

/// <summary>
/// A name used in a protocol: an LDAP attribute, a token claim, a client id. Printable ASCII
/// without spaces, so it cannot change the meaning of a filter or a request.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class IdentifierAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) =>
        value is null || (value is string text && text.Trim().All(c => c is > ' ' and <= '~' and not '(' and not ')' and not '*' and not '\\'))
            ? ValidationResult.Success
            : new ValidationResult($"{validationContext.DisplayName} may only contain letters, digits and simple punctuation, without spaces.");
}
