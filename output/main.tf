module "key-vault" {
  source              = "./modules/key-vault"
  resource_group_name = var.resource_group_name
  location            = var.location
  azure_tenant_id     = var.azure_tenant_id
  common_tags         = local.common_tags
  access_policies     = var.key_vault_access_policies
  key_vault_name      = var.key_vault_name
}
