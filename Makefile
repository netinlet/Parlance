SHELL := bash

.DEFAULT_GOAL := help

DOTNET ?= dotnet
NPM ?= npm

CONFIGURATION ?= Release
LOCAL_FEED ?= /tmp/parlance-local-feed
TEST_RESULTS_DIR ?= $(CURDIR)/.ci/test-results
ARTIFACTS_DIR ?= $(CURDIR)/artifacts
TOOL_ARTIFACTS_DIR ?= $(ARTIFACTS_DIR)/tool
TARGET_PROJECT ?= /absolute/path/to/target-repo
SOLUTION ?= YourSolution.sln
TOOL_VERSION ?= 0.1.0
TOOL_PACKAGE_ID ?= Parlance.Cli
TOOL_COMMAND_NAME ?= parlance

AGENT_CORE_DIR := src/Parlance.Agent/Core
AGENT_ADAPTER_DIR := src/Parlance.Agent/Adapter.Claude
CLI_PROJECT := src/Parlance.Cli/Parlance.Cli.csproj
MCP_PROJECT := src/Parlance.Mcp/Parlance.Mcp.csproj
ANALYZER_PROJECT := src/Parlance.CSharp.Analyzers/Parlance.CSharp.Analyzers.csproj

.PHONY: help bootstrap restore local-feed \
	agent-install-deps agent-typecheck agent-test agent-build agent-ci agent-dist-check \
	agent-install-command tool-install-command tool-install-local tool-reinstall-local tool-uninstall-local \
	format build build-cli build-mcp test test-results-dir coverage-report ci \
	pack-tool release-artifacts clean-agent clean clean-all clean-generated

help:
	@printf '%s\n' \
		'Common targets:' \
		'  make bootstrap         # restore .NET deps + npm deps for agent workspaces' \
		'  make build             # build agent bundles and the .NET solution' \
		'  make test              # run agent tests and dotnet tests' \
		'  make ci                # local equivalent of CI' \
		'  make pack-tool         # pack the parlance dotnet tool into artifacts/tool' \
		'  make tool-install-local    # install the packed tool from artifacts/tool' \
		'  make tool-reinstall-local  # reinstall the packed tool from artifacts/tool' \
		'  make tool-uninstall-local  # remove the globally installed tool' \
		'  make tool-install-command  # print the exact dotnet tool install command' \
		'  make clean             # remove repo build outputs' \
		'  make clean-generated   # remove generated agent bundles too' \
		'  make clean-all         # clean outputs plus local feed and test artifacts' \
		'' \
		'Agent targets:' \
		'  make agent-install-deps' \
		'  make agent-typecheck' \
		'  make agent-test' \
		'  make agent-build' \
		'  make agent-dist-check' \
		'  make agent-install-command TARGET_PROJECT=/repo SOLUTION=App.sln' \
		'' \
		'.NET targets:' \
		'  make restore' \
		'  make format' \
		'  make build-cli' \
		'  make build-mcp' \
		'  make test'

bootstrap: restore agent-install-deps

local-feed:
	rm -rf "$(LOCAL_FEED)"
	mkdir -p "$(LOCAL_FEED)"
	$(DOTNET) pack "$(ANALYZER_PROJECT)" --output "$(LOCAL_FEED)" --configuration "$(CONFIGURATION)"
	$(DOTNET) nuget remove source parlance-local >/dev/null 2>&1 || true
	$(DOTNET) nuget add source "$(LOCAL_FEED)" --name parlance-local

restore: local-feed
	$(DOTNET) restore Parlance.sln

agent-install-deps:
	$(MAKE) -C "$(AGENT_CORE_DIR)" install
	$(MAKE) -C "$(AGENT_ADAPTER_DIR)" install

agent-install-command:
	@printf '%s\n' \
		'dotnet run --project "$(CURDIR)/$(CLI_PROJECT)" -- \' \
		'  agent install --for claude -- \' \
		'  --project "$(TARGET_PROJECT)" \' \
		'  --solution "$(SOLUTION)"'

tool-install-command:
	@printf '%s\n' \
		'dotnet tool install -g $(TOOL_PACKAGE_ID) \' \
		'  --add-source "$(TOOL_ARTIFACTS_DIR)" \' \
		'  --version "$(TOOL_VERSION)"' \
		'' \
		'# installed command: $(TOOL_COMMAND_NAME)'

tool-install-local: pack-tool
	dotnet tool install -g "$(TOOL_PACKAGE_ID)" \
		--add-source "$(TOOL_ARTIFACTS_DIR)" \
		--version "$(TOOL_VERSION)"

tool-reinstall-local: pack-tool
	-dotnet tool uninstall -g "$(TOOL_PACKAGE_ID)"
	dotnet tool install -g "$(TOOL_PACKAGE_ID)" \
		--add-source "$(TOOL_ARTIFACTS_DIR)" \
		--version "$(TOOL_VERSION)"

tool-uninstall-local:
	dotnet tool uninstall -g "$(TOOL_PACKAGE_ID)"

agent-typecheck:
	$(MAKE) -C "$(AGENT_CORE_DIR)" typecheck
	$(MAKE) -C "$(AGENT_ADAPTER_DIR)" typecheck

agent-test:
	$(MAKE) -C "$(AGENT_CORE_DIR)" test
	$(MAKE) -C "$(AGENT_ADAPTER_DIR)" test

agent-build:
	$(MAKE) -C "$(AGENT_CORE_DIR)" build
	$(MAKE) -C "$(AGENT_ADAPTER_DIR)" build

agent-ci: agent-typecheck agent-build agent-test

agent-dist-check: agent-build
	git diff --exit-code -- "$(AGENT_CORE_DIR)/dist" "$(AGENT_ADAPTER_DIR)/dist"

format:
	$(DOTNET) format Parlance.sln --verify-no-changes --verbosity diagnostic

build: agent-build
	$(DOTNET) build Parlance.sln --configuration "$(CONFIGURATION)" --no-restore

build-cli: agent-build
	$(DOTNET) build "$(CLI_PROJECT)" --configuration "$(CONFIGURATION)" --no-restore

build-mcp:
	$(DOTNET) build "$(MCP_PROJECT)" --configuration "$(CONFIGURATION)" --no-restore

test-results-dir:
	rm -rf "$(TEST_RESULTS_DIR)"
	mkdir -p "$(TEST_RESULTS_DIR)"

test: agent-test test-results-dir
	$(DOTNET) test Parlance.sln \
		--configuration "$(CONFIGURATION)" \
		--no-build \
		--collect:"XPlat Code Coverage" \
		--results-directory "$(TEST_RESULTS_DIR)"

coverage-report:
	$(DOTNET) tool install --global dotnet-reportgenerator-globaltool
	reportgenerator \
		"-reports:$(TEST_RESULTS_DIR)/**/coverage.cobertura.xml" \
		"-targetdir:$(TEST_RESULTS_DIR)/CoverageReport" \
		-reporttypes:'Cobertura;HtmlSummary;MarkdownSummaryGithub'

ci: restore agent-install-deps agent-ci agent-dist-check format build test

pack-tool: restore agent-install-deps agent-build
	rm -rf "$(TOOL_ARTIFACTS_DIR)"
	mkdir -p "$(TOOL_ARTIFACTS_DIR)"
	$(DOTNET) pack "$(CLI_PROJECT)" \
		--configuration "$(CONFIGURATION)" \
		--no-restore \
		-o "$(TOOL_ARTIFACTS_DIR)"

release-artifacts: pack-tool

clean-agent:
	rm -rf "$(AGENT_CORE_DIR)/out-ts"
	rm -rf "$(AGENT_ADAPTER_DIR)/out-ts"

clean: clean-agent
	$(DOTNET) clean Parlance.sln --configuration "$(CONFIGURATION)" >/dev/null
	find src tests tools -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
	rm -rf "$(ARTIFACTS_DIR)" "$(TEST_RESULTS_DIR)"

clean-generated:
	rm -rf "$(AGENT_CORE_DIR)/dist" "$(AGENT_ADAPTER_DIR)/dist"

clean-all: clean clean-generated
	rm -rf "$(LOCAL_FEED)"
