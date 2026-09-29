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
# Publishing lands here first; install compares it against APP_DIR before restarting.
STAGE_DIR := /opt/blocks-function-runner.stage
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
	@rm -rf $(STAGE_DIR)
	@install -d -m 0755 $(STAGE_DIR) $(APP_DIR)
	@dotnet publish $(SRC)/Blocks.FunctionRunner/Blocks.FunctionRunner.csproj \
	  -c Release -o $(STAGE_DIR) --nologo
	@dotnet publish $(SRC)/Blocks.FunctionRunner.Cli/Blocks.FunctionRunner.Cli.csproj \
	  -c Release -o $(STAGE_DIR)/cli --nologo
	@# Publish to a staging directory and sync from there, comparing by checksum (-c) rather
	@# than timestamp: a rebuild rewrites every file with a new mtime, so an mtime comparison
	@# would call every redeploy a change and restart the runner for nothing. rsync also
	@# replaces files by rename, so the running executable is never written through.
	@changed=$$(rsync -rlpc --delete --out-format='%n' $(STAGE_DIR)/ $(APP_DIR)/ | grep -cv '/$$' || true); \
	 printf '   %s file(s) changed under %s\n' "$$changed" "$(APP_DIR)"; \
	 printf '#!/bin/sh\nexec %s/cli/fnctl "$$@"\n' '$(APP_DIR)' > /usr/local/bin/fnctl; \
	 chmod 0755 /usr/local/bin/fnctl; \
	 chown -R root:root $(APP_DIR); \
	 unit_changed=no; \
	 cmp -s $(ROOT)/deploy/$(SERVICE) /etc/systemd/system/$(SERVICE) || unit_changed=yes; \
	 install -m 0644 $(ROOT)/deploy/$(SERVICE) /etc/systemd/system/$(SERVICE); \
	 [ "$$unit_changed" = no ] || systemctl daemon-reload; \
	 systemctl enable $(SERVICE) >/dev/null 2>&1 || true; \
	 if [ "$$changed" -gt 0 ] || [ "$$unit_changed" = yes ] || [ "$${FORCE_RESTART:-no}" = yes ] \
	    || ! systemctl is-active --quiet $(SERVICE); then \
	   echo "== restarting $(SERVICE) (binaries: $$changed, unit: $$unit_changed, forced: $${FORCE_RESTART:-no})"; \
	   systemctl restart $(SERVICE); \
	 else \
	   echo "== $(SERVICE) is already running these binaries - not restarting"; \
	 fi
	@rm -rf $(STAGE_DIR)
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
