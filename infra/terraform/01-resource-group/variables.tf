variable "subscription_id" {
  description = "Azure subscription where Terraform creates resources."
  type        = string
}
variable "resource_group_name" {
  description = "Name of the learning resource group."
  type        = string
}

variable "location" {
  description = "Azure region where resources are created."
  type        = string
  default     = "West Europe"
}