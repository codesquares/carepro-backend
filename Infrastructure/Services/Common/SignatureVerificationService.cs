using Application.Interfaces.Common;
using System.Security.Cryptography;
using System.Text;

namespace Infrastructure.Services.Common
{
    public class SignatureVerificationService : ISignatureVerificationService
    {
        public bool VerifySignature(string signature, string payload, string secret)
        {
            try
            {
                if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(secret))
                    return false;

                // Remove 'sha256=' prefix if present
                var cleanSignature = signature.Replace("sha256=", "");

                // Create HMAC-SHA256 hash
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                var computedSignature = BitConverter.ToString(hash).Replace("-", "").ToLower();

                // Use constant-time comparison to prevent timing attacks
                return CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(cleanSignature),
                    Convert.FromHexString(computedSignature)
                );
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}