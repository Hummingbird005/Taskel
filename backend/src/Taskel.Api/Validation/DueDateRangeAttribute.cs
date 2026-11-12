using System.ComponentModel.DataAnnotations;

namespace Taskel.Api.Validation;

/// <summary>Keeps due dates in a sensible range, so a typo like year 20266 is rejected.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class DueDateRangeAttribute : ValidationAttribute
{
    public static readonly DateOnly Min = new(2000, 1, 1);
    public static readonly DateOnly Max = new(2100, 12, 31);

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) =>
        value is null || (value is DateOnly date && date >= Min && date <= Max)
            ? ValidationResult.Success
            : new ValidationResult($"The due date must be between {Min:yyyy-MM-dd} and {Max:yyyy-MM-dd}.");
}
