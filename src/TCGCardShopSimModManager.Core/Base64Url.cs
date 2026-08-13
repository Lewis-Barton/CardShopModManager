using System;

namespace TCGCardShopSimModManager.Core;

/// <summary>
/// URL-safe Base64 (RFC 4648 §5) used by PKCE and JWT decoding. No external
/// dependencies — just the framework converters.
/// </summary>
internal static class Base64Url
{
    public static string Encode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }

        return Convert.FromBase64String(s);
    }

    public static string DecodeToString(string text) =>
        System.Text.Encoding.UTF8.GetString(Decode(text));
}
