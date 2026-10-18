using Blocks.Genesis.DataAccess.Rules;

namespace DataAccess.InternalService;

/// <summary>A save refused by a system schema's validation rules, as a mutation response's errors.</summary>
public static class DataValidationErrors
{
    /// <summary>One message per property, keyed by its path (several messages for a property joined), as the 400 filter answers.</summary>
    public static Dictionary<string, string> ToErrors(this DataValidationException exception) =>
        exception.Result.Errors
            .GroupBy(e => e.FieldName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => string.Join("; ", g.Select(e => e.Message)), StringComparer.Ordinal);
}
