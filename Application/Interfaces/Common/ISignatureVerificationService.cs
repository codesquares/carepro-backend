using System.Security.Cryptography;
using System.Text;

namespace Application.Interfaces.Common
{
    public interface ISignatureVerificationService
    {
        bool VerifySignature(string signature, string payload, string secret);
    }
}