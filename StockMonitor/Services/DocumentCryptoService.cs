using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StockMonitor.Services;

/// <summary>
/// Exception thrown when attempting to open an encrypted portfolio document without supplying a password.
/// </summary>
public sealed class EncryptedDocumentPasswordRequiredException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="EncryptedDocumentPasswordRequiredException"/> class.</summary>
    public EncryptedDocumentPasswordRequiredException(string message) : base(message) { }
}

/// <summary>
/// Exception thrown when the supplied password cannot decrypt an encrypted portfolio document.
/// </summary>
public sealed class InvalidDocumentPasswordException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="InvalidDocumentPasswordException"/> class.</summary>
    public InvalidDocumentPasswordException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}

/// <summary>
/// Holds a derived 256-bit AES encryption key and its PBKDF2 salt in memory instead of keeping
/// the user's plaintext password as an immutable <see cref="string"/>. Zeroes all key material on <see cref="Dispose"/>.
/// </summary>
public sealed class DocumentEncryptionKey : IDisposable
{
    private readonly byte[] _key;
    private readonly byte[] _salt;
    private bool _disposed;

    internal DocumentEncryptionKey(byte[] key, byte[] salt, int iterations)
    {
        _key = key;
        _salt = salt;
        Iterations = iterations;
    }

    /// <summary>Gets the PBKDF2 iteration count used to derive this key.</summary>
    public int Iterations { get; }

    internal ReadOnlySpan<byte> KeySpan
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _key;
        }
    }

    internal ReadOnlySpan<byte> SaltSpan
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _salt;
        }
    }

    /// <summary>
    /// Derives a new 256-bit <see cref="DocumentEncryptionKey"/> from a password using PBKDF2-HMAC-SHA256.
    /// Generates a fresh 16-byte random salt if <paramref name="salt"/> is omitted.
    /// </summary>
    public static DocumentEncryptionKey DeriveFromPassword(
        string password,
        byte[]? salt = null,
        int iterations = DocumentCryptoService.DefaultIterations)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Password must not be empty.", nameof(password));
        }

        byte[] saltCopy = salt != null
            ? (byte[])salt.Clone()
            : RandomNumberGenerator.GetBytes(DocumentCryptoService.SaltSizeBytes);

        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            password,
            saltCopy,
            iterations,
            HashAlgorithmName.SHA256,
            DocumentCryptoService.KeySizeBytes);

        return new DocumentEncryptionKey(key, saltCopy, iterations);
    }

    /// <summary>
    /// Verifies in constant time whether <paramref name="candidatePassword"/> matches this derived key.
    /// </summary>
    public bool VerifyPassword(string candidatePassword)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrEmpty(candidatePassword))
        {
            return false;
        }

        byte[] candidateKey = Rfc2898DeriveBytes.Pbkdf2(
            candidatePassword,
            _salt,
            Iterations,
            HashAlgorithmName.SHA256,
            DocumentCryptoService.KeySizeBytes);

        try
        {
            return CryptographicOperations.FixedTimeEquals(candidateKey, _key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(candidateKey);
        }
    }

    /// <summary>Zeroes the derived key and salt bytes in memory.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_key);
        CryptographicOperations.ZeroMemory(_salt);
        _disposed = true;
    }
}

/// <summary>
/// Provides authenticated AES-256-GCM encryption and decryption for portfolio JSON documents
/// using PBKDF2-HMAC-SHA256 key derivation. Passwords are never persisted.
/// </summary>
public static class DocumentCryptoService
{
    private const string FormatIdentifier = "StockMonitorEncrypted";
    private const int CurrentVersion = 1;
    private const string KdfName = "PBKDF2-SHA256";
    private const string CipherName = "AES-256-GCM";
    internal const int DefaultIterations = 600_000;
    internal const int SaltSizeBytes = 16;
    internal const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    private static readonly JsonSerializerOptions EnvelopeJsonOptions = new()
    {
        WriteIndented = true
    };

    private sealed class EncryptedEnvelope
    {
        [JsonPropertyName("format")]
        public string Format { get; set; } = FormatIdentifier;

        [JsonPropertyName("version")]
        public int Version { get; set; } = CurrentVersion;

        [JsonPropertyName("kdf")]
        public string Kdf { get; set; } = KdfName;

        [JsonPropertyName("iterations")]
        public int Iterations { get; set; } = DefaultIterations;

        [JsonPropertyName("cipher")]
        public string Cipher { get; set; } = CipherName;

        [JsonPropertyName("salt")]
        public string Salt { get; set; } = string.Empty;

        [JsonPropertyName("nonce")]
        public string Nonce { get; set; } = string.Empty;

        [JsonPropertyName("tag")]
        public string Tag { get; set; } = string.Empty;

        [JsonPropertyName("ciphertext")]
        public string Ciphertext { get; set; } = string.Empty;
    }

    /// <summary>
    /// Determines whether the provided JSON string is an encrypted StockMonitor document envelope.
    /// </summary>
    public static bool IsEncryptedJson(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(content);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("format", out JsonElement formatProp) &&
                   formatProp.ValueKind == JsonValueKind.String &&
                   string.Equals(formatProp.GetString(), FormatIdentifier, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Encrypts a plaintext JSON string using AES-256-GCM with a freshly derived PBKDF2-SHA256 key
    /// and returns a serialized JSON envelope.
    /// </summary>
    public static string Encrypt(string plaintextJson, string password)
    {
        using DocumentEncryptionKey key = DocumentEncryptionKey.DeriveFromPassword(password);
        return Encrypt(plaintextJson, key);
    }

    /// <summary>
    /// Encrypts a plaintext JSON string using AES-256-GCM with a pre-derived <see cref="DocumentEncryptionKey"/>
    /// and a fresh cryptographically random 12-byte nonce on every call.
    /// </summary>
    public static string Encrypt(string plaintextJson, DocumentEncryptionKey encryptionKey)
    {
        ArgumentNullException.ThrowIfNull(plaintextJson);
        ArgumentNullException.ThrowIfNull(encryptionKey);

        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintextJson);
        byte[] ciphertext = new byte[plaintextBytes.Length];
        byte[] tag = new byte[TagSizeBytes];

        try
        {
            using AesGcm aes = new(encryptionKey.KeySpan, TagSizeBytes);
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }

        EncryptedEnvelope envelope = new()
        {
            Format = FormatIdentifier,
            Version = CurrentVersion,
            Kdf = KdfName,
            Iterations = encryptionKey.Iterations,
            Cipher = CipherName,
            Salt = Convert.ToBase64String(encryptionKey.SaltSpan),
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Ciphertext = Convert.ToBase64String(ciphertext)
        };

        return JsonSerializer.Serialize(envelope, EnvelopeJsonOptions);
    }

    /// <summary>
    /// Decrypts an encrypted StockMonitor JSON envelope using the supplied password.
    /// </summary>
    public static string Decrypt(string encryptedJson, string password)
    {
        (string plaintext, DocumentEncryptionKey derivedKey) = DecryptWithKey(encryptedJson, password);
        derivedKey.Dispose();
        return plaintext;
    }

    /// <summary>
    /// Decrypts an encrypted StockMonitor JSON envelope using the supplied password and returns
    /// both the plaintext JSON and the derived <see cref="DocumentEncryptionKey"/> so the caller
    /// can discard the plaintext password immediately.
    /// </summary>
    public static (string PlaintextJson, DocumentEncryptionKey DerivedKey) DecryptWithKey(
        string encryptedJson,
        string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new EncryptedDocumentPasswordRequiredException("A password is required to open this encrypted file.");
        }

        EncryptedEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EncryptedEnvelope>(encryptedJson, EnvelopeJsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Encrypted file format is corrupted.", ex);
        }

        if (envelope == null ||
            !string.Equals(envelope.Format, FormatIdentifier, StringComparison.Ordinal) ||
            envelope.Iterations < 10_000)
        {
            throw new InvalidDataException("Unsupported or invalid encrypted document envelope.");
        }

        byte[] salt;
        byte[] nonce;
        byte[] tag;
        byte[] ciphertext;
        try
        {
            salt = Convert.FromBase64String(envelope.Salt);
            nonce = Convert.FromBase64String(envelope.Nonce);
            tag = Convert.FromBase64String(envelope.Tag);
            ciphertext = Convert.FromBase64String(envelope.Ciphertext);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("Encrypted document contains invalid Base64 data.", ex);
        }

        if (nonce.Length != NonceSizeBytes || tag.Length != TagSizeBytes)
        {
            throw new InvalidDataException("Encrypted document has invalid cryptographic parameters.");
        }

        DocumentEncryptionKey derivedKey = DocumentEncryptionKey.DeriveFromPassword(password, salt, envelope.Iterations);
        byte[] plaintextBytes = new byte[ciphertext.Length];
        try
        {
            using AesGcm aes = new(derivedKey.KeySpan, TagSizeBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintextBytes);
            return (Encoding.UTF8.GetString(plaintextBytes), derivedKey);
        }
        catch (CryptographicException ex)
        {
            derivedKey.Dispose();
            throw new InvalidDocumentPasswordException("Invalid password or corrupted encrypted file.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }
}
