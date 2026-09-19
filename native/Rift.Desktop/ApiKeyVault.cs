using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Rift.Desktop;

// DPAPI CurrentUser binds the ciphertext to this Windows account. Never store/log plaintext.
public sealed class ApiKeyVault(string directory)
{
    private readonly string path = Path.Combine(directory, "riot-key.dpapi");
    public string? Read()
    {
        if (!File.Exists(path)) return null;
        var clear = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(clear); } finally { CryptographicOperations.ZeroMemory(clear); }
    }
    public void Save(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Saisis une clé avant de l’enregistrer.");
        var clear = Encoding.UTF8.GetBytes(key.Trim());
        try
        {
            var encrypted = ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(path + ".tmp", encrypted); File.Move(path + ".tmp", path, true);
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    public void Forget() { if (File.Exists(path)) File.Delete(path); }
}
