# Blocks Functions — Runner VM.
#
#   make provision   host provisioning (phase A-1)
#   make image       build, test and publish the Node 24 runtime image (A-2)
#   make build       build the .NET solution
#   make test        run the .NET test suite and the runtime bootstrap suite
#   make install     publish the runner to /opt and (re)start the systemd service
#   (verification is a separate harness outside this repository - see `help`)
#   make logs        follow the runner's journal

SHELL := /bin/bash
.SHELLFLAGS := -eu -o pipefail -c
.DEFAULT_GOAL := help

ROOT := $(patsubst %/,%,$(dir $(abspath $(lastword $(MAKEFILE_LIST)))))
SRC := $(ROOT)/src
APP_DIR := /opt/blocks-function-runner
SERVICE := blocks-function-runner.service
export DOTNET_CLI_TELEMETRY_OPTOUT := 1
export DOTNET_NOLOGO := 1

.PHONY: help deploy provision image build test install uninstall logs status redis-status clean

help:
	@echo "deploy:  sudo make deploy    everything: provision, image, build, test, install, verify"
	@echo "targets: provision image build test install logs status uninstall clean"
	@echo "         (provision/Makefile has the per-script host targets)"
	@echo "         verification lives outside this repository:"
	@echo "           make -C provision verify [FUNCTION_RUNNER_TESTS=<path to the harness>]"

# The one command. Everything else here is a step it runs, kept separate so a single
# phase can be repeated by hand during an incident.
deploy:
	@$(ROOT)/deploy.sh

provision:
	@$(MAKE) -C $(ROOT)/provision all

image:
	@$(ROOT)/runtime-image/build.sh

build:
	@dotnet build $(SRC)/Blocks.FunctionRunner.slnx -c Release

test:
	@echo "== .NET test suite"
	@dotnet test $(SRC)/Blocks.FunctionRunner.Tests/Blocks.FunctionRunner.Tests.csproj -c Release --nologo
	@echo "== runtime bootstrap suite (Node 24, under gVisor)"
	@docker run --rm --runtime=runsc --network=none --read-only \
	  --tmpfs /tmp:rw,exec,nosuid,nodev,size=64m --memory=512m --pids-limit=128 \
	  --cap-drop=ALL --security-opt no-new-privileges --user 10001:10001 \
	  blocks-functions-node:24-v1-test | tail -10

install: build
	@echo "== publishing to $(APP_DIR)"
	@dotnet publish $(SRC)/Blocks.FunctionRunner/Blocks.FunctionRunner.csproj \
	  -c Release -o $(APP_DIR) --nologo
	@dotnet publish $(SRC)/Blocks.FunctionRunner.Cli/Blocks.FunctionRunner.Cli.csproj \
	  -c Release -o $(APP_DIR)/cli --nologo
	@# A wrapper, not a copy: the apphost resolves its assemblies from its own directory.
	@printf '#!/bin/sh\nexec %s/cli/fnctl "$$@"\n' '$(APP_DIR)' > /usr/local/bin/fnctl
	@chmod 0755 /usr/local/bin/fnctl
	@chown -R root:root $(APP_DIR)
	@install -m 0644 $(ROOT)/deploy/$(SERVICE) /etc/systemd/system/$(SERVICE)
	@# No runner.env fallback here: provision/40-runner-user.sh has already created it,
	@# and this line could only ever run on a host that has no blocks-runner group for
	@# `install -g` to use. It was dead, and it shipped the wrong variable prefix.
	@systemctl daemon-reload
	@systemctl enable $(SERVICE)
	@# restart, not "enable --now": on an already-running unit the latter is a no-op and
	@# would leave the old binaries serving after a publish.
	@systemctl restart $(SERVICE)
	@systemctl --no-pager --lines=0 status $(SERVICE) || true

uninstall:
	@systemctl disable --now $(SERVICE) 2>/dev/null || true
	@rm -f /etc/systemd/system/$(SERVICE) /usr/local/bin/fnctl
	@systemctl daemon-reload
	@echo "removed the service (state in /var/lib/blocks-runner is kept)"

logs:
	@journalctl -u $(SERVICE) -f --no-pager

status:
	@systemctl --no-pager --lines=0 status $(SERVICE) || true
	@# fnctl no longer has a `stats` command (blocks-logic owns the queue now), so read the
	@# heartbeat hashes and stream depths straight from Redis.
	@#
	@# There is no Redis on this host any more: the runner uses the Blocks Redis over the
	@# VPN and resolves it from Key Vault, which a Makefile cannot read. Point
	@# BLOCKS_REDIS_URL at the same instance to see this section:
	@#   BLOCKS_REDIS_URL='redis://:PASSWORD@REDIS-HOST:6379' make status
	@# Note this is redis-cli's URL form, not the StackExchange connection string fnctl
	@# takes in FNCTL_REDIS — the two syntaxes are not interchangeable.
	@if [ -z "$${BLOCKS_REDIS_URL:-}" ]; then \
	  echo "runners/streams: set BLOCKS_REDIS_URL to the Blocks Redis to see queue state"; \
	else \
	  $(MAKE) --no-print-directory redis-status; \
	fi

# The queue half of `status`, split out so the BLOCKS_REDIS_URL guard above stays readable.
# Uses the host's redis-cli when present, otherwise a throwaway container on the host
# network (--rm, so nothing is left running and nothing listens).
REDIS_CLI := $(shell command -v redis-cli 2>/dev/null)
ifeq ($(REDIS_CLI),)
REDIS_CLI := docker run --rm --network host redis:8-alpine redis-cli
endif

.PHONY: redis-status
redis-status:
	@echo "runners"
	@for k in $$($(REDIS_CLI) -u "$${BLOCKS_REDIS_URL:-}" --scan --pattern 'function:runner:*' 2>/dev/null); do \
	  printf '  %s\n' "$$($(REDIS_CLI) -u "$${BLOCKS_REDIS_URL:-}" HGETALL $$k 2>/dev/null | tr -d '\r' | paste -d '=' - - | tr '\n' ' ')"; \
	done
	@echo "streams"
	@for s in functions:runs functions:results functions:builds functions:build-results functions:dead; do \
	  printf '  %-28s %s\n' "$$s" "$$($(REDIS_CLI) -u "$${BLOCKS_REDIS_URL:-}" XLEN $$s 2>/dev/null | tr -d '\r')"; \
	done

clean:
	@dotnet clean $(SRC)/Blocks.FunctionRunner.slnx -c Release --nologo || true
	@rm -rf $(SRC)/*/bin $(SRC)/*/obj
