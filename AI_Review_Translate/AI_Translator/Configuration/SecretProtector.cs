using System.Security.Cryptography;
using System.Text;

namespace AI_Translator.Configuration;

/// <summary>
/// Mã hóa / giải mã chuỗi bí mật (API key) bằng Windows DPAPI, phạm vi CurrentUser.
/// Giá trị đã mã hóa có tiền tố "enc:" để phân biệt với bản rõ cũ.
/// Lưu ý: chỉ tài khoản Windows đã mã hóa mới giải mã được (copy file sang máy/user khác sẽ không dùng được).
/// </summary>
public static class SecretProtector
{
    private const string Prefix = "enc:";

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AI_Translator.SecretProtector.v1");

    public static bool IsProtected(string? value)
    {
        return value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);
    }

    public static string Protect(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText) || IsProtected(plainText))
        {
            return plainText ?? string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(plainText);
        var cipher = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(cipher);
    }

    public static string Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value) || !IsProtected(value))
        {
            // Bản rõ cũ: trả nguyên để vẫn chạy được, sẽ được mã hóa ở lần Save tiếp theo
            return value ?? string.Empty;
        }

        try
        {
            var cipher = Convert.FromBase64String(value[Prefix.Length..]);
            var bytes = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            throw new InvalidOperationException(
                "Không thể giải mã API key trong appsettings.json (có thể file được mã hóa bởi tài khoản Windows/máy khác). " +
                "Hãy xóa giá trị \"enc:...\" và nhập lại API key.",
                ex);
        }
    }
}
