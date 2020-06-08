# Basic tool to generate .tf files out of Terraform state files (.tfstate)

## Disclaimer

This tool has been tested on a very few Azure resources such as Key Vault, Storage Account, Resource Group, Application Insights.

## Prerequisites

- Install [.NET Core 3.1 SDK](https://dotnet.microsoft.com/download/dotnet-core/3.1).
- Install [Terraform](https://www.terraform.io/downloads.html) and add it to your system's [PATH](https://superuser.com/questions/284342/what-are-path-and-other-environment-variables-and-how-can-i-set-or-use-them).

## How to start using this tool

Copy desired Terraform state file(s) (`.tfstate`) to `src` folder and launch the application.
The resulting `.tf` files will be created in `bin\Debug\netcoreapp3.1\output` folder.

Use the [import](https://www.terraform.io/docs/import/usage.html) command to create the state file for specific resource.
