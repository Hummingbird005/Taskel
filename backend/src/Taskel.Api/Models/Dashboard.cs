using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Taskel.Api.Models;

/// <summary>A user's dashboard preferences.</summary>
/// <param name="Widgets">
/// Ids of the visible widgets, in display order; null selects the default layout. The set of
/// widgets belongs to the client, so ids are only checked for shape, not against a list.
/// </param>
/// <param name="ShowDueAlert">Show the overdue / due-today reminder when the app is opened.</param>
public record DashboardSettings([WidgetIds] List<string>? Widgets, bool ShowDueAlert = true);

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed partial class WidgetIdsAttribute : ValidationAttribute
{
    public const int MaxWidgets = 30;

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null) return ValidationResult.Success;
        if (value is not IEnumerable<string?> ids) return new ValidationResult("Widgets must be a list of ids.");

        var list = ids.ToList();
        if (list.Count > MaxWidgets)
            return new ValidationResult($"A dashboard can have at most {MaxWidgets} widgets.");
        if (list.Any(id => id is null || !WidgetId().IsMatch(id)))
            return new ValidationResult("Widget ids are 1-40 lower-case letters, digits or hyphens.");
        if (list.Distinct().Count() != list.Count)
            return new ValidationResult("A widget can appear only once.");
        return ValidationResult.Success;
    }

    [GeneratedRegex("^[a-z0-9-]{1,40}$")]
    private static partial Regex WidgetId();
}
