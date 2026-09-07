using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Payroll.Validation;

/// <summary>
/// Collects every problem found in one request so a caller learns all of them at once.
/// <para>
/// Returning at the first failure would make a user correct a form one field per round trip, which
/// is the experience that makes validation resented. <see cref="DomainException"/> carries only a
/// message, so the message holds the list; a field-keyed structure would need a new exception type
/// and a change to <c>ExceptionHandlingMiddleware</c>, which is more than this needs.
/// </para>
/// </summary>
public sealed class ValidationFailures
{
    private readonly List<string> _messages = [];

    public void AddIf(bool isInvalid, string message)
    {
        if (isInvalid)
            _messages.Add(message);
    }

    /// <summary>
    /// The three rules every caller-supplied money field shares: not negative, not absurd, and not
    /// carrying precision the database will silently drop. Payroll money columns are
    /// <c>numeric(18,2)</c>, so a third decimal place is rounded away on save - the stored figure
    /// then differs from the submitted one with nothing reporting it.
    /// </summary>
    public void AddMoney(string fieldName, decimal value, decimal max)
    {
        AddIf(value < 0m, $"{fieldName} cannot be negative.");
        AddIf(value > max, $"{fieldName} cannot exceed {max:N2}.");
        AddIf(decimal.Round(value, 2) != value, $"{fieldName} cannot have more than 2 decimal places.");
    }

    public void ThrowIfAny()
    {
        if (_messages.Count > 0)
            throw new DomainException(string.Join(" ", _messages));
    }
}
