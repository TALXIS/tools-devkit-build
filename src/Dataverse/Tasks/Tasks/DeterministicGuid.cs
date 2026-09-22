using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Shared helper for deriving a stable GUID string from a seed, so repeated builds produce the
/// same id for the same seed instead of a fresh one every time. Used by GenerateGenPageFileXml
/// and GenerateConnectorXml. Not RFC 4122-conformant - nothing here requires that, only stability.
/// </summary>
internal static class DeterministicGuid
{
    public static string Create(string seed)
    {
        using (var md5 = MD5.Create())
        {
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(seed));
            var hex = System.BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            return hex.Substring(0, 8) + "-"
                 + hex.Substring(8, 4) + "-"
                 + hex.Substring(12, 4) + "-"
                 + hex.Substring(16, 4) + "-"
                 + hex.Substring(20, 12);
        }
    }
}
