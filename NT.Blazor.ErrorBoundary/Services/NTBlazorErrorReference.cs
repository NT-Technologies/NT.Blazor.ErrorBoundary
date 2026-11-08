using System.Security.Cryptography;

namespace NT.Blazor.ErrorBoundary.Services;

internal static class NTBlazorErrorReference {
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int Length = 8;

    public static string Create() {
        Span<byte> bytes = stackalloc byte[Length];
        RandomNumberGenerator.Fill(bytes);

        Span<char> chars = stackalloc char[Length];
        for (var i = 0; i < Length; i++) {
            chars[i] = Alphabet[bytes[i] & 31];
        }

        return new string(chars);
    }
}
