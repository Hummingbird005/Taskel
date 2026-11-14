namespace Taskel.Api.Data;

/// <summary>Helpers for case-insensitive comparisons that PostgreSQL runs (ILIKE).</summary>
public static class Like
{
    /// <summary>
    /// A pattern that matches <paramref name="value"/> exactly, ignoring case: its wildcard and escape
    /// characters are escaped, so <c>EF.Functions.ILike(column, Like.Exactly(value))</c> means "equal, any case".
    /// </summary>
    public static string Exactly(string value) =>
        value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
