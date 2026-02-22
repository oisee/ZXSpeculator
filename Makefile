SOLUTION     = Speculator/Speculator.sln
PROJECT      = Speculator/Speculator/Speculator.csproj
CONFIG       = Release
ARCH        ?= osx-arm64
PUBLISH_DIR  = Speculator/Speculator/bin/$(CONFIG)/net8.0/$(ARCH)/publish
INSTALL_DIR ?= $(HOME)/.local/bin
BIN_NAME     = zxs

.PHONY: build run test publish install clean

build:
	dotnet build $(SOLUTION)

run:
	dotnet run --project $(PROJECT)

test:
	dotnet test $(SOLUTION)

publish:
	dotnet publish $(PROJECT) -c $(CONFIG) -r $(ARCH) --self-contained

install: publish
	mkdir -p $(INSTALL_DIR)
	ln -sf $(CURDIR)/$(PUBLISH_DIR)/Speculator $(INSTALL_DIR)/$(BIN_NAME)
	@echo "Installed: $(INSTALL_DIR)/$(BIN_NAME) -> $(CURDIR)/$(PUBLISH_DIR)/Speculator"

clean:
	dotnet clean $(SOLUTION)
	rm -rf Speculator/Speculator/bin Speculator/Speculator/obj
	rm -rf Speculator/Speculator.Core/bin Speculator/Speculator.Core/obj
	rm -rf Speculator/CSharp.Core/bin Speculator/CSharp.Core/obj
	rm -rf Speculator/UnitTests/bin Speculator/UnitTests/obj

uninstall:
	rm -f $(INSTALL_DIR)/$(BIN_NAME)
	@echo "Removed: $(INSTALL_DIR)/$(BIN_NAME)"
