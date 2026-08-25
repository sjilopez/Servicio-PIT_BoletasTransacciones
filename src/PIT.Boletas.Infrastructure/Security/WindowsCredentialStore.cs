using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PIT.Boletas.Infrastructure.Security;

[SupportedOSPlatform("windows")]
public static class WindowsCredentialStore
{
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    private static readonly IReadOnlyDictionary<string, string> Targets = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["MySql:ConnectionString"] = "PIT_BoletasTransacciones/MySqlConnectionString",
        ["AzureFiles:ConnectionString"] = "PIT_BoletasTransacciones/AzureFilesConnectionString",
        ["AzureBlob:ConnectionString"] = "PIT_BoletasTransacciones/AzureBlobConnectionString",
        ["ExternalOcr:ApiKey"] = "PIT_BoletasTransacciones/ExternalOcrApiKey",
        ["Alerts:TeamsWebhook"] = "PIT_BoletasTransacciones/TeamsWebhook",
        ["Alerts:SmtpUser"] = "PIT_BoletasTransacciones/SmtpUser",
        ["Alerts:SmtpPassword"] = "PIT_BoletasTransacciones/SmtpPassword"
    };

    public static void MigrateLegacySettings(string legacyPath, string currentPath)
    {
        if (File.Exists(currentPath) || !File.Exists(legacyPath))
        {
            return;
        }

        JsonNode settings = JsonNode.Parse(File.ReadAllText(legacyPath))
            ?? throw new InvalidOperationException("El archivo de configuracion antiguo esta vacio.");

        foreach ((string configurationKey, string target) in Targets)
        {
            JsonNode? value = GetNode(settings, configurationKey);
            if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out string? secret) && !string.IsNullOrWhiteSpace(secret))
            {
                Write(target, secret);
                RemoveNode(settings, configurationKey);
            }
        }

        string? directory = Path.GetDirectoryName(currentPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = currentPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, currentPath);
            File.Delete(legacyPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static Dictionary<string, string> LoadConfigurationOverrides()
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string configurationKey, string target) in Targets)
        {
            string? value = Read(target);
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[configurationKey] = value;
            }
        }

        return values;
    }

    public static void ProvisionInteractive(TextWriter output, TextWriter error)
    {
        output.WriteLine("Provision de secretos PIT_BoletasTransacciones para LocalSystem.");
        output.WriteLine("Deje vacio un valor para conservar el secreto existente.");

        foreach ((string configurationKey, string target) in Targets)
        {
            string? current = Read(target);
            output.Write($"{configurationKey}: ");
            string? value = ReadSecret();

            if (string.IsNullOrWhiteSpace(value))
            {
                if (current is null)
                {
                    error.WriteLine($"No se registro {configurationKey}.");
                }

                continue;
            }

            Write(target, value);
            output.WriteLine("Guardado en Credential Manager.");
        }
    }

    private static string? Read(string target)
    {
        if (!CredRead(target, CredentialTypeGeneric, 0, out IntPtr credentialPointer))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return null;
            }

            throw new InvalidOperationException($"No se pudo leer la credencial {target}. Win32: {error}.");
        }

        try
        {
            NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero)
            {
                return null;
            }

            byte[] secret = new byte[checked((int)credential.CredentialBlobSize)];
            Marshal.Copy(credential.CredentialBlob, secret, 0, secret.Length);
            return Encoding.Unicode.GetString(secret);
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    private static void Write(string target, string value)
    {
        byte[] secret = Encoding.Unicode.GetBytes(value);
        IntPtr targetPointer = Marshal.StringToCoTaskMemUni(target);
        IntPtr userPointer = Marshal.StringToCoTaskMemUni("PIT_BoletasTransacciones");
        IntPtr secretPointer = Marshal.AllocCoTaskMem(secret.Length);

        try
        {
            Marshal.Copy(secret, 0, secretPointer, secret.Length);
            NativeCredential credential = new()
            {
                Type = CredentialTypeGeneric,
                TargetName = targetPointer,
                UserName = userPointer,
                CredentialBlob = secretPointer,
                CredentialBlobSize = (uint)secret.Length,
                Persist = CredentialPersistLocalMachine
            };

            if (!CredWrite(ref credential, 0))
            {
                int error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"No se pudo guardar la credencial {target}. Win32: {error}.");
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(targetPointer);
            Marshal.FreeCoTaskMem(userPointer);
            Marshal.FreeCoTaskMem(secretPointer);
        }
    }

    private static string ReadSecret()
    {
        StringBuilder value = new();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return value.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Length > 0)
                {
                    value.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                value.Append(key.KeyChar);
            }
        }
    }

    private static JsonNode? GetNode(JsonNode root, string path)
    {
        JsonNode? current = root;
        foreach (string segment in path.Split(':'))
        {
            current = current?[segment];
        }

        return current;
    }

    private static void RemoveNode(JsonNode root, string path)
    {
        string[] segments = path.Split(':');
        JsonObject? parent = root.AsObject();
        for (int index = 0; index < segments.Length - 1; index++)
        {
            parent = parent[segments[index]]?.AsObject();
            if (parent is null)
            {
                return;
            }
        }

        parent.Remove(segments[^1]);
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential userCredential, uint flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CredFree([In] IntPtr credential);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }
}