resource "azurerm_key_vault" "key-vault" {
  name                            = var.key_vault_name
  resource_group_name             = var.resource_group_name
  location                        = var.location
  enabled_for_deployment          = true
  enabled_for_disk_encryption     = true
  enabled_for_template_deployment = true
  tenant_id                       = var.azure_tenant_id
  tags                            = var.common_tags
  sku_name                        = "standard"

 dynamic "access_policy" {
    for_each = var.access_policies
    content {
      tenant_id               = var.azure_tenant_id
      object_id               = access_policy.value.object_id
      key_permissions         = access_policy.value.key_permissions
      secret_permissions      = access_policy.value.secret_permissions
      certificate_permissions = access_policy.value.certificate_permissions
    }
  }
}

resource "azurerm_key_vault_certificate" "key-vault-certificate" {
  # (resource arguments)
}

resource "azurerm_key_vault_secret" "key-vault-secret" {
  # (resource arguments)
}

resource "azurerm_key_vault_key" "key-vault-key" {
  # (resource arguments)
}
