using System;
using System.Data;
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

        private static async Task Main(string[] args)
        {
            var currentDirectory = Directory.GetCurrentDirectory();

            var fileNames = Directory.EnumerateFiles(
                currentDirectory,
                "*.tfstate",
                SearchOption.TopDirectoryOnly
                );

            foreach (var fileName in fileNames)
            {
                await Transform(fileName);
            }

            FormatFiles();
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
                        ? $@"resource ""{resource["type"]}"" ""{resource["name"]}_{instance["index_key"]}"" {{"
                        : $@"resource ""{resource["type"]}"" ""{resource["name"]}"" {{");

                    var attribute = instance["attributes"];

                    var attributeObj = JObject.Parse(attribute.ToString());
                    foreach (var property in attributeObj.Properties())
                    {
                        if (property.Name.Equals("id", StringComparison.OrdinalIgnoreCase)) continue;
                        if (property.First.Type == JTokenType.Array)
                        {
                            if (property.Children().All(c => c.First == null)) continue;

                            var elements = property.Value.ToArray();

                            foreach (var element in elements)
                            {
                                sb.AppendLine($"{property.Name} {{");

                                foreach (var child in element.Children())
                                {
                                    AppendProperty((JProperty)child, sb);
                                }

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

        private static void AppendProperty(JProperty property, StringBuilder sb)
        {
            if (property.First.Type == JTokenType.Array)
            {
                foreach (var jToken in property.Children())
                {
                    //Traverse(jToken.First ?? jToken, property, sb);
                    Traverse2(jToken, sb);
                }

                return;
            }

            var val = property.Value.ToString();

            if (string.IsNullOrEmpty(val)) return;

            Append(property, sb);
        }

        private static JTokenType[] _simpleTypes =
        {
            JTokenType.Boolean, JTokenType.Bytes, JTokenType.Date, JTokenType.Float,
            JTokenType.Guid, JTokenType.Uri, JTokenType.Integer, JTokenType.String,
            JTokenType.TimeSpan
        };
        private static void Traverse2(JToken token, StringBuilder sb)
        {
            if (_simpleTypes.Contains(token.Type))
            {
                Append(token as JProperty, sb);
                return;
            }

            foreach (var child in token.Children())
            {
                Traverse2(child, sb);
            }
        }

        //private static void Traverse(JToken jToken, JProperty property, StringBuilder sb)
        //{
        //    if (jToken.Type != JTokenType.Array
        //        && jToken.Type != JTokenType.Object
        //        && jToken.Type != JTokenType.Property)
        //    {
        //        Append(property, sb);
        //        return;
        //    }

        //    if (jToken.First?.Type != JTokenType.Array
        //        && jToken.First?.Type != JTokenType.Object
        //        && jToken.First?.Type != JTokenType.Property)
        //    {
        //        Append(property, sb);
        //        return;
        //    }

        //    sb.AppendLine($"{property.Name} {{");

        //    foreach (var child in jToken.Children())
        //    {
        //        var prop = child.Type == JTokenType.Property ? (JProperty) child : (JProperty) child.First;
        //        Traverse(child.First ?? child, prop, sb);
        //    }

        //    sb.AppendLine("}");
        //}

        private static void Append(JProperty property, StringBuilder sb)
        {
            var propertyValue = property.Value.ToString();
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

        private static async Task SaveFile(string outputFileName, string result)
        {
            if (!Directory.Exists(OutputDirectoryName))
            {
                Directory.CreateDirectory(OutputDirectoryName);
            }

            await File.WriteAllTextAsync($"{OutputDirectoryName}/{outputFileName}.tf", result, Encoding.UTF8);
        }

        private static void FormatFiles()
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
