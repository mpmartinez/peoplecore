using System.Security.Cryptography;

namespace PeopleCore.API.Accounts;

/// <summary>
/// The one-time password Admin or HR hands to a user when creating their account or resetting it.
/// The user must replace it at sign-in, so its job is to be unguessable and easy to pass on: letters
/// and digits only, none of the pairs people misread (0/O, 1/l/I).
/// </summary>
public static class TemporaryPasswordGenerator
{
    public const int Length = 16;

    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Any = Upper + Lower + Digits;

    public static string Generate()
    {
        var chars = new char[Length];

        // One of each class guarantees the policy is met; the shuffle stops them always leading.
        chars[0] = Pick(Upper);
        chars[1] = Pick(Lower);
        chars[2] = Pick(Digits);
        for (var i = 3; i < Length; i++)
            chars[i] = Pick(Any);

        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }

    private static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
}
