# Makefile — local build and publish helpers.
#
# Packaging targets for the .NET SDK. CI builds the same package in
# .github/workflows/build-artifacts.yml but never pushes it (CRS-004); these
# targets are the manual publication route.

SHELL := /bin/bash
.SHELLFLAGS := -eu -o pipefail -c

DOTNET_PROJECT := dotnet/BastionVault.IntegrationSdk/BastionVault.IntegrationSdk.csproj
PACKAGE_OUTPUT := artifacts/dotnet
CLOUDSMITH_NUGET_SOURCE := https://nuget.cloudsmith.io/uox/bastionvault/v3/index.json

.DEFAULT_GOAL := help

.PHONY: help package package-publish

## help: list the available targets (default)
help:
	@echo "Targets:"
	@grep -E '^## [a-zA-Z0-9_-]+:' $(MAKEFILE_LIST) \
	  | sed -e 's/^## //' \
	  | awk -F ': *' '{ printf "  \033[1m%-16s\033[0m %s\n", $$1, $$2 }'

## package: build the .NET NuGet package into artifacts/dotnet
package:
	rm -rf $(PACKAGE_OUTPUT)
	dotnet pack $(DOTNET_PROJECT) -c Release -p:ContinuousIntegrationBuild=true -p:PackageOutputPath=$(CURDIR)/$(PACKAGE_OUTPUT)
	@ls -1 $(PACKAGE_OUTPUT)/*.nupkg

## package-publish: build, then push to the Cloudsmith bastionvault repository
package-publish: package
	@if [ -n "$${CLOUDSMITH_API_KEY:-}" ]; then \
	   key="$$CLOUDSMITH_API_KEY"; \
	   echo "Using CLOUDSMITH_API_KEY from the environment."; \
	 else \
	   read -rsp "Cloudsmith API key: " key < /dev/tty; echo; \
	 fi; \
	 if [ -z "$$key" ]; then echo "error: no API key supplied, nothing pushed." >&2; exit 1; fi; \
	 for pkg in $(PACKAGE_OUTPUT)/*.nupkg; do \
	   echo "Pushing $$pkg to $(CLOUDSMITH_NUGET_SOURCE)"; \
	   dotnet nuget push "$$pkg" --api-key "$$key" --source $(CLOUDSMITH_NUGET_SOURCE); \
	 done
