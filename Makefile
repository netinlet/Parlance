SHELL := bash

.DEFAULT_GOAL := help

DOTNET ?= dotnet
NPM ?= npm
NODE20_NPM ?= npx -p node@20 -p npm@10 npm

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
AGENT_ADAPTER_DIRS := src/Parlance.Agent/Adapter.Claude src/Parlance.Agent/Adapter.Codex
AGENT_DIST_DIRS := $(AGENT_CORE_DIR)/dist src/Parlance.Agent/Adapter.Claude/dist src/Parlance.Agent/Adapter.Codex/dist
CLI_PROJECT := src/Parlance.Cli/Parlance.Cli.csproj
MCP_PROJECT := src/Parlance.Mcp/Parlance.Mcp.csproj
ANALYZER_PROJECT := src/Parlance.CSharp.Analyzers/Parlance.CSharp.Analyzers.csproj

.PHONY: help bootstrap restore local-feed \
	agent-install-deps agent-lock-refresh agent-lock-check agent-typecheck agent-test agent-build agent-ci agent-dist-check \
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
		'  make agent-lock-refresh  # refresh agent package-lock.json files with Node 20 / npm 10' \
		'  make agent-lock-check    # verify agent lockfiles are stable under Node 20 / npm 10' \
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
	@set -e; for dir in $(AGENT_ADAPTER_DIRS); do $(MAKE) -C "$$dir" install; done

agent-lock-refresh:
	cd "$(AGENT_CORE_DIR)" && rm -rf node_modules && $(NODE20_NPM) install --package-lock-only
	@set -e; for dir in $(AGENT_ADAPTER_DIRS); do cd "$(CURDIR)/$$dir" && rm -rf node_modules && $(NODE20_NPM) install --package-lock-only; done

agent-lock-check: agent-lock-refresh
	git diff --exit-code -- "$(AGENT_CORE_DIR)/package-lock.json" src/Parlance.Agent/Adapter.Claude/package-lock.json src/Parlance.Agent/Adapter.Codex/package-lock.json

agent-install-command:
	@printf '%s\n' \
		'dotnet run --project "$(CURDIR)/$(CLI_PROJECT)" -- \' \
		'  agent install --for claude -- \' \
		'  --project "$(TARGET_PROJECT)" \' \
		'  --solution "$(SOLUTION)"' \
		'' \
		'dotnet run --project "$(CURDIR)/$(CLI_PROJECT)" -- \' \
		'  agent install --for codex -- \' \
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

agent-typecheck: agent-install-deps
	$(MAKE) -C "$(AGENT_CORE_DIR)" typecheck
	@set -e; for dir in $(AGENT_ADAPTER_DIRS); do $(MAKE) -C "$$dir" typecheck; done

agent-test: agent-install-deps
	$(MAKE) -C "$(AGENT_CORE_DIR)" test
	@set -e; for dir in $(AGENT_ADAPTER_DIRS); do $(MAKE) -C "$$dir" test; done

agent-build: agent-install-deps
	$(MAKE) -C "$(AGENT_CORE_DIR)" build
	@set -e; for dir in $(AGENT_ADAPTER_DIRS); do $(MAKE) -C "$$dir" build; done

agent-ci: agent-typecheck agent-build agent-test

agent-dist-check: agent-build
	git diff --exit-code -- $(AGENT_DIST_DIRS)

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

ci: restore agent-ci agent-dist-check format build test

pack-tool: restore agent-build
	rm -rf "$(TOOL_ARTIFACTS_DIR)"
	mkdir -p "$(TOOL_ARTIFACTS_DIR)"
	$(DOTNET) pack "$(CLI_PROJECT)" \
		--configuration "$(CONFIGURATION)" \
		--no-restore \
		-o "$(TOOL_ARTIFACTS_DIR)"

release-artifacts: pack-tool

clean-agent:
	rm -rf "$(AGENT_CORE_DIR)/out-ts"
	@set -e; for dir in $(AGENT_ADAPTER_DIRS); do rm -rf "$$dir/out-ts"; done

clean: clean-agent
	$(DOTNET) clean Parlance.sln --configuration "$(CONFIGURATION)" >/dev/null
	find src tests tools -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
	rm -rf "$(ARTIFACTS_DIR)" "$(TEST_RESULTS_DIR)"

clean-generated:
	rm -rf $(AGENT_DIST_DIRS)

clean-all: clean clean-generated
	rm -rf "$(LOCAL_FEED)"
