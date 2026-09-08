using System.Globalization;

namespace IAS.Client.Components.UI;

/// <summary>
/// Converts between the strings an <c>&lt;input&gt;</c>/<c>&lt;select&gt;</c> element exchanges with
/// the browser and the strongly typed values the form components bind to.
/// </summary>
/// <remarks>
/// The form components accept any bindable type so pages can bind straight to their models
/// (<see cref="DateOnly"/> dates, <see cref="Guid"/> foreign keys, decimals) instead of hand-rolling
/// raw elements with string round-tripping. Formatting is invariant because the wire formats the
/// browser expects — <c>yyyy-MM-dd</c> for date inputs, <c>HH:mm</c> for time inputs, an invariant
/// decimal point for number inputs — are fixed regardless of the user's locale.
/// </remarks>
internal static class BindValue
{
    public static string? Format<T>(T? value, string? inputType = null)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return value switch
        {
            string s => s,
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly t => t.ToString("HH:mm", CultureInfo.InvariantCulture),
            DateTime dt => inputType == "date"
                ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : dt.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }

    public static bool TryParse<T>(string? text, out T? result)
    {
        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        if (string.IsNullOrEmpty(text))
        {
            // A cleared text control means the empty string, not null, so bound string
            // properties never flip to null just because the user erased them. Other targets
            // can only represent "cleared" when they are nullable; a non-nullable one keeps
            // whatever it had rather than snapping to a default.
            if (target == typeof(string))
            {
                result = (T)(object)string.Empty;
                return true;
            }

            result = default;
            return Nullable.GetUnderlyingType(typeof(T)) is not null;
        }

        object? parsed;
        var culture = CultureInfo.InvariantCulture;

        if (target == typeof(string))
        {
            parsed = text;
        }
        else if (target.IsEnum)
        {
            if (!Enum.TryParse(target, text, ignoreCase: true, out parsed))
            {
                result = default;
                return false;
            }
        }
        else if (target == typeof(Guid))
        {
            parsed = Guid.TryParse(text, out var g) ? g : null;
        }
        else if (target == typeof(DateOnly))
        {
            parsed = DateOnly.TryParse(text, culture, DateTimeStyles.None, out var d) ? d : null;
        }
        else if (target == typeof(TimeOnly))
        {
            parsed = TimeOnly.TryParse(text, culture, DateTimeStyles.None, out var t) ? t : null;
        }
        else if (target == typeof(DateTime))
        {
            parsed = DateTime.TryParse(text, culture, DateTimeStyles.None, out var dt) ? dt : null;
        }
        else if (target == typeof(DateTimeOffset))
        {
            parsed = DateTimeOffset.TryParse(text, culture, DateTimeStyles.None, out var dto) ? dto : null;
        }
        else if (target == typeof(bool))
        {
            parsed = bool.TryParse(text, out var b) ? b : null;
        }
        else
        {
            try
            {
                parsed = Convert.ChangeType(text, target, culture);
            }
            catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException)
            {
                parsed = null;
            }
        }

        if (parsed is null)
        {
            result = default;
            return false;
        }

        result = (T)parsed;
        return true;
    }
}
