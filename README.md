# assms-dispatch-service

## Overview

Dispatch owns technician coverage and later assignment decisions. Sprint 2
starts with the Technician creation contract in
[`docs/technician-management/US-05A-create-technician.md`](docs/technician-management/US-05A-create-technician.md).

## Responsibilities

## Technology

## Project Structure

## Local Development

## Environment Variables

## Testing

## Deployment

## Documentation

| Method | Path | Purpose |
| --- | --- | --- |
| `POST` | `/api/technicians` | Create a Dispatch-owned technician record. |
| `GET` | `/api/technicians/{id}` | Retrieve a technician by Dispatch id. |

## Database Ownership

## Kafka Responsibilities

## Terraform Infrastructure

This repository owns only the Dispatch Service VM, NIC, service-specific NSG, and optional public IP. Shared resource-group and services-subnet values are consumed from the platform Terraform remote state or supplied through explicit overrides; this repository does not recreate the shared VNet or subnets.

## Continuous Integration

GitHub Actions runs on pull requests targeting `dev` or `main` and pushes to `dev` or `main`. CI validates Terraform formatting and both environment roots, then restores and builds the .NET 8 solution in Release mode and runs its tests with the configured XPlat coverage collector. Mandatory failures fail CI; test results and coverage are retained as workflow artifacts. No deployment occurs from this workflow.
