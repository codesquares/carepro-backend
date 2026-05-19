using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Native .NET 8 implementation of the Web Push Protocol (RFC 8030),
    /// VAPID authentication (RFC 8292), and aes128gcm content encryption (RFC 8188/8291).
    /// No third-party BouncyCastle dependency required.
    /// </summary>
    internal static class WebPushHelper
    {
        // -----------------------------------------------------------------------
        // Public entry point
        // -----------------------------------------------------------------------

        /// <summary>
        /// Encrypts the payload and sends a single Web Push HTTP request.
        /// Throws <see cref="WebPushDeliveryException"/> on non-2xx responses.
        /// </summary>
        public static async Task SendAsync(
            HttpClient http,
            string endpoint,
            string p256dhBase64Url,
            string authBase64Url,
            string vapidPublicKeyBase64Url,
            string vapidPrivateKeyBase64Url,
            string vapidSubject,
            string payloadJson,
            CancellationToken ct = default)
        {
            byte[] clientPublicKey = Base64UrlDecode(p256dhBase64Url);   // 65 bytes
            byte[] authSecret      = Base64UrlDecode(authBase64Url);     // 16 bytes

            // Encrypt content per RFC 8291 / RFC 8188 (aes128gcm)
            byte[] encryptedBody = Encrypt(
                Encoding.UTF8.GetBytes(payloadJson),
                clientPublicKey,
                authSecret);

            // Build VAPID JWT for the push service origin
            var audience = new Uri(endpoint).GetLeftPart(UriPartial.Authority);
            string jwt = BuildVapidJwt(audience, vapidPrivateKeyBase64Url, vapidPublicKeyBase64Url, vapidSubject);

            // HTTP POST to the push endpoint
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.TryAddWithoutValidation(
                "Authorization", $"vapid t={jwt},k={vapidPublicKeyBase64Url}");
            request.Headers.TryAddWithoutValidation("TTL", "86400");

            request.Content = new ByteArrayContent(encryptedBody);
            request.Content.Headers.ContentType =
                new MediaTypeHeaderValue("application/octet-stream");
            request.Content.Headers.TryAddWithoutValidation("Content-Encoding", "aes128gcm");

            using var response = await http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new WebPushDeliveryException(
                    response.StatusCode,
                    $"Push delivery failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }
        }

        // -----------------------------------------------------------------------
        // RFC 8291 / RFC 8188 — aes128gcm content encryption
        // -----------------------------------------------------------------------

        private static byte[] Encrypt(
            byte[] plaintext,
            byte[] clientPublicKeyBytes,   // 65-byte uncompressed P-256 point
            byte[] authSecret)             // 16-byte auth secret from subscription
        {
            // 1. Generate ephemeral server ECDH P-256 key pair
            using var serverEcDh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

            // Export server public key as 65-byte uncompressed point (0x04 || X || Y)
            var serverPubParams = serverEcDh.ExportParameters(false);
            var serverPublicKey = new byte[65];
            serverPublicKey[0] = 0x04;
            Buffer.BlockCopy(serverPubParams.Q.X!, 0, serverPublicKey, 1,  32);
            Buffer.BlockCopy(serverPubParams.Q.Y!, 0, serverPublicKey, 33, 32);

            // 2. Import client public key (raw 65-byte uncompressed P-256 point)
            using var clientEcDh = ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint
                {
                    X = clientPublicKeyBytes[1..33],
                    Y = clientPublicKeyBytes[33..65]
                }
            });

            // 3. ECDH: shared secret (raw 32 bytes)
            byte[] sharedSecret = serverEcDh.DeriveRawSecretAgreement(clientEcDh.PublicKey);

            // 4. RFC 8291 §3.2: combine shared secret + auth secret
            //    PRK_key = HKDF-Extract(salt=auth_secret, IKM=shared_secret)
            byte[] prkKey = HKDF.Extract(HashAlgorithmName.SHA256, sharedSecret, authSecret);

            //    info   = "WebPush: info\0" || client_pubkey(65) || server_pubkey(65)
            byte[] infoKey = new byte[14 + 65 + 65];
            Encoding.ASCII.GetBytes("WebPush: info\0").CopyTo(infoKey, 0);
            clientPublicKeyBytes.CopyTo(infoKey, 14);
            serverPublicKey.CopyTo(infoKey, 14 + 65);

            //    IKM = HKDF-Expand(PRK_key, info, 32)
            byte[] ikm = HKDF.Expand(HashAlgorithmName.SHA256, prkKey, 32, infoKey);

            // 5. Generate random 16-byte salt for content encoding
            byte[] salt = RandomNumberGenerator.GetBytes(16);

            // 6. RFC 8188: derive CEK and nonce from random salt + IKM
            //    PRK = HKDF-Extract(salt=random_salt, IKM=ikm)
            byte[] prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);

            //    CEK   = HKDF-Expand(PRK, "Content-Encoding: aes128gcm\0", 16)
            byte[] cek = HKDF.Expand(
                HashAlgorithmName.SHA256, prk, 16,
                Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));

            //    Nonce = HKDF-Expand(PRK, "Content-Encoding: nonce\0", 12)
            byte[] nonce = HKDF.Expand(
                HashAlgorithmName.SHA256, prk, 12,
                Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

            // 7. Pad plaintext with record delimiter 0x02 (single record, RFC 8188 §2.1)
            byte[] padded = new byte[plaintext.Length + 1];
            plaintext.CopyTo(padded, 0);
            padded[plaintext.Length] = 0x02;

            // 8. AES-128-GCM encrypt
            byte[] ciphertext = new byte[padded.Length];
            byte[] tag        = new byte[16];
            using var aesGcm = new AesGcm(cek, 16);
            aesGcm.Encrypt(nonce, padded, ciphertext, tag);

            // 9. Build binary header per RFC 8188 §2.1:
            //    salt(16) || rs(uint32 big-endian) || idlen(1) || server_public_key(65)
            const uint recordSize = 4096;
            byte[] header = new byte[16 + 4 + 1 + 65];
            salt.CopyTo(header, 0);
            header[16] = (byte)((recordSize >> 24) & 0xFF);
            header[17] = (byte)((recordSize >> 16) & 0xFF);
            header[18] = (byte)((recordSize >> 8)  & 0xFF);
            header[19] = (byte)(recordSize         & 0xFF);
            header[20] = 65; // key ID length
            serverPublicKey.CopyTo(header, 21);

            // 10. Final body = header || ciphertext || tag
            byte[] body = new byte[header.Length + ciphertext.Length + tag.Length];
            header.CopyTo(body, 0);
            ciphertext.CopyTo(body, header.Length);
            tag.CopyTo(body, header.Length + ciphertext.Length);

            return body;
        }

        // -----------------------------------------------------------------------
        // RFC 8292 — VAPID JWT (ES256 / ECDSA P-256)
        // -----------------------------------------------------------------------

        private static string BuildVapidJwt(
            string audience,
            string vapidPrivateKeyBase64Url,   // raw 32-byte P-256 private scalar
            string vapidPublicKeyBase64Url,    // raw 65-byte uncompressed P-256 public key
            string subject)
        {
            var headerJson  = JsonSerializer.SerializeToUtf8Bytes(new { typ = "JWT", alg = "ES256" });
            var payloadJson = JsonSerializer.SerializeToUtf8Bytes(new
            {
                aud = audience,
                exp = DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds(),
                sub = subject
            });

            string headerB64  = Base64UrlEncode(headerJson);
            string payloadB64 = Base64UrlEncode(payloadJson);
            byte[] signingInput = Encoding.ASCII.GetBytes($"{headerB64}.{payloadB64}");

            // Build ECParameters from the raw private scalar + public point
            byte[] d   = Base64UrlDecode(vapidPrivateKeyBase64Url);   // 32 bytes
            byte[] pub = Base64UrlDecode(vapidPublicKeyBase64Url);    // 65 bytes (0x04 || x || y)

            var ecParams = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                D = d,
                Q = new ECPoint
                {
                    X = pub[1..33],
                    Y = pub[33..65]
                }
            };

            using var ecdsa = ECDsa.Create(ecParams);
            // IEEE P1363 fixed-field concatenation is required for JWT ES256
            byte[] sig = ecdsa.SignData(
                signingInput,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

            return $"{headerB64}.{payloadB64}.{Base64UrlEncode(sig)}";
        }

        // -----------------------------------------------------------------------
        // Base64URL helpers
        // -----------------------------------------------------------------------

        internal static byte[] Base64UrlDecode(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "=";  break;
            }
            return Convert.FromBase64String(s);
        }

        internal static string Base64UrlEncode(byte[] data) =>
            Convert.ToBase64String(data)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
    }

    /// <summary>
    /// Thrown when the push service returns a non-2xx HTTP response.
    /// Check <see cref="StatusCode"/> — 404/410 means the subscription is expired.
    /// </summary>
    internal sealed class WebPushDeliveryException : Exception
    {
        public HttpStatusCode StatusCode { get; }

        public WebPushDeliveryException(HttpStatusCode statusCode, string message)
            : base(message)
        {
            StatusCode = statusCode;
        }
    }
}
