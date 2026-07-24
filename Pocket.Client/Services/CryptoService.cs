using System.Security.Cryptography;
using System.Text;

namespace Pocket.Client.Services
{
    public interface ICryptoService
    {
        (string PublicKey, string PrivateKey) GenerateKeyPair();
        byte[] DeriveSharedSecret(string myPrivateKeyBase64, string peerPublicKeyBase64);
        byte[] EncryptData(byte[] data, byte[] sharedSecret);
        byte[] DecryptData(byte[] encryptedData, byte[] sharedSecret);
    }

    public class CryptoService : ICryptoService
    {
        public (string PublicKey, string PrivateKey) GenerateKeyPair()
        {
            using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            var publicKey = Convert.ToBase64String(ecdh.ExportSubjectPublicKeyInfo());
            var privateKey = Convert.ToBase64String(ecdh.ExportECPrivateKey());
            return (publicKey, privateKey);
        }

        public byte[] DeriveSharedSecret(string myPrivateKeyBase64, string peerPublicKeyBase64)
        {
            using var myEcdh = ECDiffieHellman.Create();
            myEcdh.ImportECPrivateKey(Convert.FromBase64String(myPrivateKeyBase64), out _);

            using var peerEcdh = ECDiffieHellman.Create();
            peerEcdh.ImportSubjectPublicKeyInfo(Convert.FromBase64String(peerPublicKeyBase64), out _);

            return myEcdh.DeriveKeyMaterial(peerEcdh.PublicKey);
        }

        public byte[] EncryptData(byte[] data, byte[] sharedSecret)
        {
            const int tagSize = 16;
            const int nonceSize = 12;

            using var aes = new AesGcm(sharedSecret, tagSize);
            
            // AES-GCM requires a nonce (IV). We'll generate a random one.
            var nonce = new byte[nonceSize];
            RandomNumberGenerator.Fill(nonce);

            var tag = new byte[tagSize];
            var cipherText = new byte[data.Length];

            aes.Encrypt(nonce, data, cipherText, tag);

            // Combine nonce + tag + ciphertext to return as a single payload
            var result = new byte[nonce.Length + tag.Length + cipherText.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
            Buffer.BlockCopy(tag, 0, result, nonce.Length, tag.Length);
            Buffer.BlockCopy(cipherText, 0, result, nonce.Length + tag.Length, cipherText.Length);

            return result;
        }

        public byte[] DecryptData(byte[] encryptedData, byte[] sharedSecret)
        {
            const int tagSize = 16;
            const int nonceSize = 12;

            using var aes = new AesGcm(sharedSecret, tagSize);

            var nonce = new byte[nonceSize];
            var tag = new byte[tagSize];
            var cipherText = new byte[encryptedData.Length - nonceSize - tagSize];

            Buffer.BlockCopy(encryptedData, 0, nonce, 0, nonceSize);
            Buffer.BlockCopy(encryptedData, nonceSize, tag, 0, tagSize);
            Buffer.BlockCopy(encryptedData, nonceSize + tagSize, cipherText, 0, cipherText.Length);

            var decryptedData = new byte[cipherText.Length];
            aes.Decrypt(nonce, cipherText, tag, decryptedData);

            return decryptedData;
        }
    }
}
