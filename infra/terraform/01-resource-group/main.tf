terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
  }
}
resource "azurerm_resource_group" "learning" {
  name     = var.resource_group_name
  location = var.location

  tags = {
    environment = "development"
    managed_by  = "terraform"
    project     = "AzureOrderProcessing"
  }
}