using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace TerraformStateToHcl
{
    internal class Program
    {
        private const string OutputDirectoryName = "output";
        private const string TerraformStateFileExtension = ".tfstate";

        private static readonly JTokenType[] SimpleTypes =
        {
            JTokenType.Boolean, 
            JTokenType.Bytes, 
            JTokenType.Date, 
            JTokenType.Float,
            JTokenType.Guid, 
            JTokenType.Uri, 
            JTokenType.Integer, 
            JTokenType.String,
            JTokenType.TimeSpan
        };

        private static readonly string[] ExcludedPropertiesStartingWith =
        {
            "primary_",
            "secondary_",
        };

        private static readonly string[] ExcludedPropertiesExactMatch =
        {
            "id",
            "vault_uri",
            "fully_qualified_domain_name",
            "sku",
            "secret_id",
            "certificate_data",
            "thumbprint",
            "version",
            "days_before_expiry",
            "e",
            "n",
            "x",
            "y",
            "account_type"
        };

        private static async Task Main()
        {
            PrepareOutputDirectory();

            //await new KeyVaultImporter().Import();

            var fileNames = GetTerraformStateFileNames();

            if (!fileNames.Any())
            {
                Console.WriteLine($"Terraform state files ({TerraformStateFileExtension}) do not exist in current directory.");
                return;
            }

            foreach (var fileName in fileNames)
            {
                Console.WriteLine($"Processing {fileName}...");
                await Transform(fileName);
                Console.WriteLine($"Finished processing {fileName}.");
            }

            FormatResultFiles();
        }

        private static void PrepareOutputDirectory()
        {
            string[] extensionsToDelete = { ".txt", ".ps1", ".tfstate", ".backup" };
            var terraformDirectory = Path.Combine(OutputDirectoryName, ".terraform");
            if (Directory.Exists(terraformDirectory))
            {
                Directory.Delete(terraformDirectory, true);
            }

            var directory = new DirectoryInfo(OutputDirectoryName);

            foreach (var file in directory
                .EnumerateFiles()
                .Where(d => extensionsToDelete.Contains(d.Extension.ToLowerInvariant())))
            {
                file.Delete(); 
            }
        }

        private static string[] GetTerraformStateFileNames()
        {
            var currentProjectDirectory =
                Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\.."));

            var fileNames = Directory.EnumerateFiles(
                currentProjectDirectory,
                $"*{TerraformStateFileExtension}",
                SearchOption.TopDirectoryOnly
            );

            return fileNames.ToArray();
        }

        private static async Task Transform(string fileName)
        {
            var outputFileName = string.Empty;

            var stateFileObj = await GetTerraformStateFileJson(fileName);

            var sb = new StringBuilder();

            foreach (var resource in stateFileObj["resources"])
            {
                outputFileName = resource["module"].ToString().Split('.', StringSplitOptions.RemoveEmptyEntries)[1];

                foreach (var instance in resource["instances"])
                {
                    sb.AppendLine(instance["index_key"] != null
                        ? $@"resource ""{resource["type"]}"" ""{instance["index_key"]}"" {{"
                        : $@"resource ""{resource["type"]}"" ""{resource["name"]}"" {{");

                    var attribute = instance["attributes"];

                    var attributeObj = JObject.Parse(attribute.ToString());
                    foreach (var property in attributeObj.Properties())
                    {
                        if (ContainsExcludedProperty(property)) continue;

                        if (property.First.Type == JTokenType.Array
                            && !property.First.Children().All(c => SimpleTypes.Contains(c.Type)))
                        {
                            if (property.Children().All(c => c.First == null)) continue;

                            foreach (var child in property.First.Children())
                            {
                                sb.AppendLine($"{property.Name} {{");

                                AppendProperty(child, sb);

                                sb.AppendLine("}");
                            }
                        }
                        else
                        {
                            AppendProperty(property, sb);
                        }
                    }

                    sb.AppendLine("}");
                }
            }

            var result = sb.ToString();
            Console.WriteLine(result);

            await SaveFile(outputFileName, result);
        }

        private static async Task<JObject> GetTerraformStateFileJson(string fileName)
        {
            var stateFileContents = await File.ReadAllTextAsync(fileName);

            var stateFileObj = JObject.Parse(stateFileContents);

            return stateFileObj;
        }

        private static void AppendProperty(JToken token, StringBuilder sb)
        {
            switch (token.Type)
            {
                case JTokenType.Property:
                    {
                        var property = (JProperty) token;

                        if (ContainsExcludedProperty(property)) return;

                        if (property.Value.ToString().Equals("[]"))
                        {
                            return;
                        }

                        if (property
                            .Children()
                            .Any(c => c.Type == JTokenType.Array && c.Children()
                                          .Any(cc => !SimpleTypes.Contains(cc.Type))))
                        {
                            sb.AppendLine($"{property.Name} {{");

                            AppendProperty(token.First, sb);

                            sb.AppendLine("}");

                            return;
                        }

                        Append(property, sb);
                        break;
                    }
                case JTokenType.Array:
                case JTokenType.Object:
                    {
                        foreach (var child in token.Children())
                        {
                            AppendProperty(child, sb);
                        }

                        break;
                    }
            }
        }

        private static void Append(JProperty property, StringBuilder sb)
        {
            var propertyValue = property.Value.ToString();

            if (string.IsNullOrEmpty(propertyValue)) return;

            switch (property.First.Type)
            {
                case JTokenType.Array:
                case JTokenType.Object:
                    sb.AppendLine($"{property.Name} = {propertyValue}");
                    break;
                case JTokenType.Boolean:
                    sb.AppendLine($"{property.Name} = {propertyValue.ToLowerInvariant()}");
                    break;
                default:
                    sb.AppendLine($"{property.Name} = \"{propertyValue}\"");
                    break;
            }
        }

        private static bool ContainsExcludedProperty(JProperty property)
        {
            return ExcludedPropertiesStartingWith
                       .Any(prop => property.Name.StartsWith(prop, StringComparison.OrdinalIgnoreCase))
                   ||
                   ExcludedPropertiesExactMatch
                       .Any(prop => prop.Equals(property.Name, StringComparison.OrdinalIgnoreCase));

        }

        private static async Task SaveFile(string outputFileName, string result)
        {
            await File.WriteAllTextAsync($"{OutputDirectoryName}/{outputFileName}.tf", result, Encoding.UTF8);
        }

        private static void FormatResultFiles()
        {
            using var terraform = new Process
            {
                StartInfo =
                {
                    UseShellExecute = false,
                    FileName = "terraform.exe",
                    ArgumentList = { "fmt" },
                    CreateNoWindow = true,
                    WorkingDirectory = OutputDirectoryName
                }
            };

            terraform.Start();
            terraform.WaitForExit();
            terraform.Close();
        }
    }
}
