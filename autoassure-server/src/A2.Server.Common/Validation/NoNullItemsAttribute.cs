using System.Collections;
using System.ComponentModel.DataAnnotations;

namespace A2.Server.Common;

/// <summary>
/// Validation attribute for collections and dictionaries that must not contain
/// null items. The collection itself can be null (use [Required] to prevent that).
/// Works on IEnumerable and dictionary values.
/// </summary>
public class NoNullItemsAttribute()
    : ValidationAttribute("Items must not be null.")
{
    public override bool IsValid(object? value)
    {
        if (value is null)
            return true;

        // Check if it's a dictionary (IReadOnlyDictionary or IDictionary)
        if (value is IDictionary dict)
        {
            foreach (var dictValue in dict.Values)
            {
                if (dictValue is null)
                    return false;
            }

            return true;
        }

        // Check if it's an IEnumerable (but not string, which is also IEnumerable)
        if (value is IEnumerable enumerable && value is not string)
        {
            foreach (var item in enumerable)
            {
                if (item is null)
                    return false;
            }

            // ReSharper disable once DuplicatedStatements -- intentional: all paths return true if valid
            return true;
        }

        return true;
    }
}
