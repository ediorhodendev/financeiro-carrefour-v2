using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Ledger.Application;

public static class IdempotencyHash
{
    // Raw UTF-8 payload is intentional: even whitespace changes are distinct request bodies.
    public static string Compute(string operation, string body) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(operation + "\n" + body)));
    public static void ValidateKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || key.Any(char.IsControl))
            throw new ArgumentException("Provide one nonblank Idempotency-Key with at most 128 characters.");
    }
}
