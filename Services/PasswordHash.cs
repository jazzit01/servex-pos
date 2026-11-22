using System;
using System.Security.Cryptography;

namespace ServeX.Services
{
    internal static class PasswordHash
    {
        private const int Iterations = 600_000;
        private const int SaltSize = 16;
        private const int HashSize = 32;

        public static string Create(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = Derive(password, salt);
            return $"pbkdf2-sha256-v1:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
        }

        public static bool Verify(string password, string stored)
        {
            var parts = stored.Split(':');
            if (parts.Length != 3 || parts[0] != "pbkdf2-sha256-v1") return false;

            Span<byte> salt = stackalloc byte[SaltSize];
            Span<byte> expected = stackalloc byte[HashSize];
            return Convert.TryFromBase64String(parts[1], salt, out int saltLength) && saltLength == SaltSize &&
                   Convert.TryFromBase64String(parts[2], expected, out int hashLength) && hashLength == HashSize &&
                   CryptographicOperations.FixedTimeEquals(expected, Derive(password, salt));
        }

        private static byte[] Derive(string password, ReadOnlySpan<byte> salt) =>
            Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
    }
}
