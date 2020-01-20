using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.Services.AppAuthentication;

namespace TerraformStateToHcl
{
    public class KeyVaultImporter
    {
        private const string KeyVaultUrl = "https://if-dev-secrets.vault.azure.net/";
        private const string OutputDirectoryName = "output";

        private const string CertificateResourceType = "azurerm_key_vault_certificate";
        private const string CertificateResourceName = "key-vault-certificate";

        private const string KeyResourceType = "azurerm_key_vault_key";
        private const string KeyResourceName = "key-vault-key";

        private const string SecretResourceType = "azurerm_key_vault_secret";
        private const string SecretResourceName = "key-vault-secret";

        public async Task Import()
        {
            // TODO: Improve performance.
            var stopwatch = Stopwatch.StartNew();
            var azureServiceTokenProvider = new AzureServiceTokenProvider();
            var client = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(azureServiceTokenProvider.KeyVaultTokenCallback));

            var certificateIdsAndNames = await GetCertificateIdsAndNames(client);
            var keyIdsAndNames = await GetKeyIdsAndNames(client);
            var secretIdsAndNames = await GetSecretIdsAndNames(client);

            var commands = $"terraform init{Environment.NewLine}";

            commands += GenerateKeyVaultImportCommand();

            commands += GenerateTerraformImportCommands(
                certificateIdsAndNames,
                CertificateResourceType,
                CertificateResourceName
                );

            commands += GenerateTerraformImportCommands(
                keyIdsAndNames,
                KeyResourceType,
                KeyResourceName
                );

            commands += GenerateTerraformImportCommands(
                secretIdsAndNames,
                SecretResourceType,
                SecretResourceName
                );

            await ExecuteTerraformImportCommands(commands);

            stopwatch.Stop();

            Console.WriteLine("State import finished.");
            Console.WriteLine($"Time spent: {stopwatch.Elapsed}");
        }

        private static string GenerateKeyVaultImportCommand()
        {
            return @$"terraform import 'module.key-vault.azurerm_key_vault.key-vault' /subscriptions/77981029-1d2f-4323-b715-879564056947/resourceGroups/OBS-West-dev/providers/Microsoft.KeyVault/vaults/if-dev-secrets{Environment.NewLine}";
        }

        private static async Task<IReadOnlyDictionary<string, string>> GetCertificateIdsAndNames(IKeyVaultClient client)
        {
            var certificateIdsAndNames = new Dictionary<string, string>();

            var certificates = await client.GetCertificatesAsync(KeyVaultUrl);

            var certificateNames = certificates.Select(c => c.Identifier.Name).ToArray();

            Console.WriteLine($"Certificates: {Environment.NewLine}{string.Join(Environment.NewLine, certificateNames)}{Environment.NewLine}");

            foreach (var certificateName in certificateNames)
            {
                var certificateId = await GetCertificateId(client, certificateName);
                certificateIdsAndNames.Add(certificateId, certificateName);
            }

            return certificateIdsAndNames;
        }

        private static async Task<string> GetCertificateId(IKeyVaultClient client, string certificateName)
        {
            var certificate = await client.GetCertificateAsync(KeyVaultUrl, certificateName);
            return RemovePortIfDefault(new Uri(certificate.Id)).ToString();
        }

        private static async Task<IReadOnlyDictionary<string, string>> GetKeyIdsAndNames(IKeyVaultClient client)
        {
            var idsAndNames = new Dictionary<string, string>();

            var keys = await client.GetKeysAsync(KeyVaultUrl);

            var filteredKeyNames = keys
                .Where(k => !k.Managed.HasValue)
                .Select(k => k.Identifier.Name)
                .ToArray();

            Console.WriteLine($"Keys: {Environment.NewLine}{string.Join(Environment.NewLine, filteredKeyNames)}{Environment.NewLine}");

            foreach (var keyName in filteredKeyNames)
            {
                var keyId = await GetKeyId(client, keyName);
                idsAndNames.Add(keyId, keyName);
            }

            return idsAndNames;
        }

        private static async Task<string> GetKeyId(IKeyVaultClient client, string keyName)
        {
            var key = await client.GetKeyAsync(KeyVaultUrl, keyName);
            return RemovePortIfDefault(new Uri(key.KeyIdentifier.Identifier)).ToString();
        }

        private static async Task<IReadOnlyDictionary<string, string>> GetSecretIdsAndNames(IKeyVaultClient client)
        {
            var idsAndNames = new Dictionary<string, string>();

            var secrets = await client.GetSecretsAsync(KeyVaultUrl);

            var filteredSecretNames = secrets
                .Where(s => !s.Managed.HasValue)
                .Select(s => s.Identifier.Name)
                .ToArray();

            Console.WriteLine($"Secrets: {Environment.NewLine}{string.Join(Environment.NewLine, filteredSecretNames)}{Environment.NewLine}");

            foreach (var secretName in filteredSecretNames)
            {
                var secretId = await GetSecretId(client, secretName);
                idsAndNames.Add(secretId, secretName);
            }

            return idsAndNames;
        }

        private static async Task<string> GetSecretId(IKeyVaultClient client, string secretName)
        {
            var secret = await client.GetSecretAsync(KeyVaultUrl, secretName);
            return RemovePortIfDefault(new Uri(secret.SecretIdentifier.Identifier)).ToString();
        }

        private static Uri RemovePortIfDefault(Uri uri)
        {
            if (!uri.IsDefaultPort || uri.Port == -1) return uri;

            var builder = new UriBuilder(uri) { Port = -1 };
            return builder.Uri;
        }

        private static string GenerateTerraformImportCommands(
            IReadOnlyDictionary<string, string> idsAndNames,
            string resourceType,
            string resourceName
            )
        {
            var sb = new StringBuilder();

            foreach (var (key, value) in idsAndNames)
            {
                sb.AppendLine(@$"terraform import 'module.key-vault.{resourceType}.{resourceName}[\""{value}\""]' {key}");
            }

            sb.AppendLine();

            var result = sb.ToString();
            Console.WriteLine(result);
            return result;
        }

        private static async Task ExecuteTerraformImportCommands(string commands)
        {
            var errorData = new StringBuilder();
            var outputData = new StringBuilder();

            await File.WriteAllTextAsync($"{OutputDirectoryName}/import.ps1", commands, Encoding.UTF8);

            using var terraform = new Process
            {
                StartInfo =
                {
                    UseShellExecute = false,
                    FileName = "powershell.exe",
                    Arguments = @".\import.ps1",
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    WorkingDirectory = OutputDirectoryName
                }
            };

            terraform.ErrorDataReceived += (sender, args) =>
            {
                if (string.IsNullOrEmpty(args.Data)) return;

                errorData.AppendLine(args.Data);
            };

            terraform.OutputDataReceived += (sender, args) =>
            {
                if (string.IsNullOrEmpty(args.Data)) return;

                outputData.Append(args.Data);
            };

            terraform.Start();

            terraform.BeginOutputReadLine();
            terraform.BeginErrorReadLine();

            terraform.WaitForExit();
            terraform.Close();

            await File.WriteAllTextAsync($"{OutputDirectoryName}/import-errors.txt", errorData.ToString(), Encoding.UTF8);
            await File.WriteAllTextAsync($"{OutputDirectoryName}/import-output.txt", outputData.ToString(), Encoding.UTF8);
        }
    }
}