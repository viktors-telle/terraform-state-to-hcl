using System.Threading.Tasks;

namespace TerraformStateToHcl
{
    internal static class Program
    {
        private static async Task Main()
        {
            await new JsonToHclConverter().Convert();
        }
    }
}
