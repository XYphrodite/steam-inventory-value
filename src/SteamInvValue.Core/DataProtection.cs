using System.Runtime.InteropServices;
using System.Text;

namespace SteamInvValue.Core;

/// <summary>
/// Шифрование секретов ключом текущей учётной записи Windows (DPAPI).
///
/// Нужно ради cookie <c>steamLoginSecure</c>: это пропуск действующей сессии Steam, и лежать
/// открытым текстом в конфиге он не должен — файл уезжает с копией папки, бэкапом или
/// синхронизацией. После шифрования такой файл бесполезен на другой машине и под другой
/// учёткой. От вредоноса, который уже работает под тобой, это не спасает: он попросит Windows
/// расшифровать и получит то же самое.
///
/// Вызывается crypt32 напрямую — пакет System.Security.Cryptography.ProtectedData дал бы то
/// же самое, но у проекта нет ни одной зависимости, и заводить её ради тридцати строк незачем.
/// </summary>
public static class DataProtection
{
    private const string Prefix = "dpapi:";

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description,
        IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description,
        IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    public static bool IsProtected(string? value) =>
        value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Шифрует, если можем. На неподдерживаемой системе возвращает как есть.</summary>
    public static string Protect(string value)
    {
        if (IsProtected(value) || !OperatingSystem.IsWindows()) return value;

        var bytes = Encoding.UTF8.GetBytes(value);
        var input = new DataBlob();
        var output = new DataBlob();

        try
        {
            input.Size = bytes.Length;
            input.Data = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);

            if (!CryptProtectData(ref input, "SteamInvValue", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out output))
                return value;

            var encrypted = new byte[output.Size];
            Marshal.Copy(output.Data, encrypted, 0, output.Size);
            return Prefix + Convert.ToBase64String(encrypted);
        }
        catch { return value; }
        finally
        {
            if (input.Data != IntPtr.Zero) Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }

    /// <summary>Расшифровывает; незашифрованное значение отдаёт как есть.</summary>
    public static string Unprotect(string value)
    {
        if (!IsProtected(value) || !OperatingSystem.IsWindows()) return value;

        var input = new DataBlob();
        var output = new DataBlob();

        try
        {
            var bytes = Convert.FromBase64String(value[Prefix.Length..]);
            input.Size = bytes.Length;
            input.Data = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);

            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out output))
                return "";

            var decrypted = new byte[output.Size];
            Marshal.Copy(output.Data, decrypted, 0, output.Size);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch { return ""; }
        finally
        {
            if (input.Data != IntPtr.Zero) Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }
}
