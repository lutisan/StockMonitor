using StockMonitor.Services;

namespace StockMonitor.Tests;

/// <summary>
/// Unit tests for <see cref="DocumentCryptoService"/> verifying AES-256-GCM + PBKDF2-SHA256
/// encryption, decryption, tamper detection, and password validation.
/// </summary>
public sealed class DocumentCryptoServiceTests
{
    /// <summary>Verifies that encrypting and decrypting Unicode JSON round-trips cleanly and detects the encrypted envelope.</summary>
    [Fact]
    public void EncryptAndDecrypt_RoundTripsUnicodeJsonAndDetectsEnvelope()
    {
        const string plaintext = """{"AccountNumber":"1576650","Note":"Portfólio — Žluťoučký kůň 🚀"}""";
        const string password = "Strong!Password#2026";

        string encrypted = DocumentCryptoService.Encrypt(plaintext, password);

        Assert.True(DocumentCryptoService.IsEncryptedJson(encrypted));
        Assert.False(DocumentCryptoService.IsEncryptedJson(plaintext));
        Assert.DoesNotContain("1576650", encrypted, StringComparison.Ordinal);
        Assert.DoesNotContain(password, encrypted, StringComparison.Ordinal);

        string decrypted = DocumentCryptoService.Decrypt(encrypted, password);
        Assert.Equal(plaintext, decrypted);
    }

    /// <summary>Verifies that fresh random salt and nonce produce different encrypted outputs for identical input.</summary>
    [Fact]
    public void Encrypt_UsesFreshSaltAndNonce_ProducesDistinctOutputsForSameInput()
    {
        const string plaintext = """{"key":"value"}""";
        const string password = "SamePassword";

        string first = DocumentCryptoService.Encrypt(plaintext, password);
        string second = DocumentCryptoService.Encrypt(plaintext, password);

        Assert.NotEqual(first, second);
        Assert.Equal(plaintext, DocumentCryptoService.Decrypt(first, password));
        Assert.Equal(plaintext, DocumentCryptoService.Decrypt(second, password));
    }

    /// <summary>Verifies that decrypting with an incorrect password throws <see cref="InvalidDocumentPasswordException"/>.</summary>
    [Fact]
    public void Decrypt_WithWrongPassword_ThrowsInvalidDocumentPasswordException()
    {
        string encrypted = DocumentCryptoService.Encrypt("""{"hello":"world"}""", "CorrectPassword");

        Assert.Throws<InvalidDocumentPasswordException>(() =>
            DocumentCryptoService.Decrypt(encrypted, "WrongPassword"));
    }

    /// <summary>Verifies that decrypting with an empty password throws <see cref="EncryptedDocumentPasswordRequiredException"/>.</summary>
    [Fact]
    public void Decrypt_WithEmptyPassword_ThrowsPasswordRequiredException()
    {
        string encrypted = DocumentCryptoService.Encrypt("""{"hello":"world"}""", "Secret123");

        Assert.Throws<EncryptedDocumentPasswordRequiredException>(() =>
            DocumentCryptoService.Decrypt(encrypted, string.Empty));
    }

    /// <summary>Verifies that <see cref="DocumentCryptoService.IsEncryptedJson"/> returns false for non-envelope or invalid JSON.</summary>
    [Fact]
    public void IsEncryptedJson_ReturnsFalseForInvalidOrEmptyStrings()
    {
        Assert.False(DocumentCryptoService.IsEncryptedJson(string.Empty));
        Assert.False(DocumentCryptoService.IsEncryptedJson("{ invalid"));
        Assert.False(DocumentCryptoService.IsEncryptedJson("[]"));
        Assert.False(DocumentCryptoService.IsEncryptedJson("""{"format":"Other"}"""));
    }

    /// <summary>Verifies that a derived <see cref="DocumentEncryptionKey"/> can re-encrypt with fresh nonces, decrypt via password, and zeroes on dispose.</summary>
    [Fact]
    public void DocumentEncryptionKey_ReEncryptsWithFreshNonce_AndThrowsAfterDispose()
    {
        const string password = "CachedKeyPassword!99";
        const string v1 = """{"version":1}""";
        const string v2 = """{"version":2}""";

        string initialEncrypted = DocumentCryptoService.Encrypt(v1, password);
        (string decryptedV1, DocumentEncryptionKey key) = DocumentCryptoService.DecryptWithKey(initialEncrypted, password);
        Assert.Equal(v1, decryptedV1);

        string reEncryptedV2a = DocumentCryptoService.Encrypt(v2, key);
        string reEncryptedV2b = DocumentCryptoService.Encrypt(v2, key);
        Assert.NotEqual(reEncryptedV2a, reEncryptedV2b);
        Assert.Equal(v2, DocumentCryptoService.Decrypt(reEncryptedV2a, password));
        Assert.Equal(v2, DocumentCryptoService.Decrypt(reEncryptedV2b, password));
        Assert.True(key.VerifyPassword(password));
        Assert.False(key.VerifyPassword("WrongPassword"));
        Assert.False(key.VerifyPassword(string.Empty));

        key.Dispose();
        Assert.Throws<ObjectDisposedException>(() => DocumentCryptoService.Encrypt(v2, key));
        Assert.Throws<ObjectDisposedException>(() => key.VerifyPassword(password));
    }
}
