# Dispatch Service Staging Infrastructure

## Current Infrastructure

| Item | Value |
|---|---|
| VM | `vm-assms-dispatch-staging` |
| Region | Central India |
| SKU / architecture | `Standard_B2pls_v2`, Arm64, 2 vCPU / 4 GiB |
| OS | Ubuntu 22.04 Arm64 with Standard_LRS OS disk |
| Private network | Secondary services subnet `10.30.1.0/24`; assigned private IP `10.30.1.4` |
| Public IP | Static Standard resource exists for future controlled access |
| State key | `dispatch-service/staging.tfstate` |
| Platform dependency | Reads `platform/staging.tfstate`, including `secondary_services_subnet_id` |

The NSG has no custom inbound rules. Password authentication is disabled, SSH is not exposed, and Azure default deny-inbound remains effective. The secondary subnet can use private VNet peering to reach Kafka at `10.20.2.4:9092` and private MySQL DNS.

## Migration History

The initial Southeast Asia apply partially created Dispatch networking resources but could not create the VM because the regional vCPU quota was exhausted. Terraform preserved remote state and later replaced only Dispatch-owned NSG, public IP, NIC, and association resources during the Central India migration.

## Application Status and ARM64 Note

The Dispatch API, technician/assignment features, Docker runtime, and Kafka client integration are **not deployed**. The VM is currently deallocated for cost control. Future Docker images and any future native/Kafka dependencies must be tested for Arm64 compatibility.

## Future Deployment Work

Start the VM, configure restricted deployment access, build/test the Docker image, deploy the API, configure reviewed private MySQL/Kafka settings, and verify health checks. No application port or secret is currently configured.
