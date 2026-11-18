DOTNET_VERSION := 10
FREEDESKTOP_VERSION := 26.08
PROJECT := ../../PRoCon.UI.csproj

BUILDER_TOOLS_URL := https://github.com/flatpak/flatpak-builder-tools.git
BUILDER_TOOLS_COMMIT := 338ce9c6512d49d98ae9a508d219ffe19b5144e8

.PHONY: regenerate-sources

regenerate-sources:
	rm -rf /tmp/flatpak-builder-tools
	git clone $(BUILDER_TOOLS_URL) /tmp/flatpak-builder-tools
	cd /tmp/flatpak-builder-tools && git checkout $(BUILDER_TOOLS_COMMIT)
	python3 /tmp/flatpak-builder-tools/dotnet/flatpak-dotnet-generator.py \
		--dotnet $(DOTNET_VERSION) \
		--freedesktop $(FREEDESKTOP_VERSION) \
		nuget-sources.json $(PROJECT)
	rm -rf /tmp/flatpak-builder-tools
