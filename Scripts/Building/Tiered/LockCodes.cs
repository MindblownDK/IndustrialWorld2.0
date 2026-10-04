// Assets/Scripts/VoxelEngine/Building/Tiered/LockCodes.cs
//
// 14.56.0 - code lock combinations stop existing in plain text.
//
// Storage format, one string, carried in the same field/save slot the old
// plaintext used (old saves convert on load):
//
//     "sha256:<salt>:<hash>"     full form - authority machines (host, offline)
//     "sha256:<salt>:"           public form - what guests ever see: enough to
//                                hash a keypad attempt locally, never enough to
//                                recover or verify the combination
//
// Rules this helper enforces across the whole lock system:
//   - the PLAIN code never leaves the machine it was typed on: setting a code
//     hashes locally and ships salt+hash; trying a code hashes locally with
//     the lock's replicated salt and ships only the attempt hash
//   - the HASH never travels host -> guest: state broadcasts, join snapshots
//     and anything a guest can read carry the public form only
//   - verification happens ONLY where the full form lives: the host

using System;
using System.Security.Cryptography;
using System.Text;

namespace VoxelEngine.Building.Tiered
{
    public static class LockCodes
    {
        public const string Prefix = "sha256:";

        public static string NewSalt() => Guid.NewGuid().ToString("N");

        /// <summary>Hex SHA-256 of "salt:code". Deterministic across machines.</summary>
        public static string Hash(string salt, string code)
        {
            using var sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes((salt ?? "") + ":" + (code ?? "")));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public static string Pack(string salt, string hash) => Prefix + salt + ":" + (hash ?? "");

        public static bool LooksPacked(string stored)
            => !string.IsNullOrEmpty(stored) && stored.StartsWith(Prefix, StringComparison.Ordinal);

        public static bool TryUnpack(string stored, out string salt, out string hash)
        {
            salt = ""; hash = "";
            if (!LooksPacked(stored)) return false;
            string body = stored.Substring(Prefix.Length);
            int sep = body.IndexOf(':');
            if (sep < 0) return false;
            salt = body.Substring(0, sep);
            hash = body.Substring(sep + 1);
            return true;
        }

        public static string SaltOf(string stored)
            => TryUnpack(stored, out var salt, out _) ? salt : "";

        /// <summary>The guest-safe face of a stored code: salt kept (attempts
        /// need it), hash stripped. Empty stays empty.</summary>
        public static string PublicForm(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            return TryUnpack(stored, out var salt, out _)
                ? Pack(salt, "")
                : Pack(NewSalt(), "");   // legacy plaintext: never expose it, even here
        }

        /// <summary>Load-time normalization: legacy plaintext codes (pre-14.56.0
        /// saves) become salted hashes the first time they are read.</summary>
        public static string Canonicalize(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            if (LooksPacked(stored)) return stored;
            string salt = NewSalt();
            return Pack(salt, Hash(salt, stored));
        }

        /// <summary>Authority check: does the plain attempt open this stored code?</summary>
        public static bool Matches(string stored, string attempt)
        {
            if (!TryUnpack(stored, out var salt, out var hash) || hash.Length == 0) return false;
            return string.Equals(Hash(salt, attempt), hash, StringComparison.Ordinal);
        }

        /// <summary>Authority check for a pre-hashed attempt (guests hash with
        /// the lock's replicated salt before sending).</summary>
        public static bool MatchesHash(string stored, string attemptHash)
        {
            if (!TryUnpack(stored, out _, out var hash) || hash.Length == 0) return false;
            return !string.IsNullOrEmpty(attemptHash)
                && string.Equals(attemptHash, hash, StringComparison.OrdinalIgnoreCase);
        }
    }
}
