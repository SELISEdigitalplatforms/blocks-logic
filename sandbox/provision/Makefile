# Blocks Functions — Runner VM host provisioning (phase A-1).
#
#   make            everything, in order
#   make preflight  inspect the host and record provision/.facts (changes nothing)
#   make verify     run the A-1 checkpoint against the live host
#
# Every target is idempotent: re-running is the supported way to reconcile the host after
# editing /etc/blocks-runner/deny-cidrs, /etc/blocks-runner/dns-servers,
# /etc/blocks-runner/logging.conf or .versions.

SHELL := /bin/bash
.SHELLFLAGS := -eu -o pipefail -c
.DEFAULT_GOAL := all

ROOT := $(patsubst %/,%,$(dir $(abspath $(lastword $(MAKEFILE_LIST)))))
# The verification harness deliberately lives outside this repository so no test material can
# reach a VM (see README). The default is a checkout beside blocks-logic itself: a plain sibling
# of sandbox/ would now be *inside* the repository, which is exactly what must not happen.
# Point FUNCTION_RUNNER_TESTS somewhere else if yours is elsewhere:
#   make verify FUNCTION_RUNNER_TESTS=/path/to/FunctionRunnerTests
FUNCTION_RUNNER_TESTS ?= $(ROOT)/../../../FunctionRunnerTests
VERIFY := $(FUNCTION_RUNNER_TESTS)/verify/verify.sh

.PHONY: all preflight docker gvisor network runner-user registry dotnet logging \
        verify verify-host verify-sandbox verify-network \
        facts firewall-show help

# `all` provisions the host and stops there: it must not depend on the test harness being
# checked out next to it. Run `make verify` separately, or `../deploy.sh` for the lot.
all: preflight docker gvisor network runner-user registry dotnet logging

preflight:   ; @$(ROOT)/00-preflight.sh
docker:      ; @$(ROOT)/10-docker.sh
gvisor:      ; @$(ROOT)/20-gvisor.sh
network:     ; @$(ROOT)/30-network.sh
runner-user: ; @$(ROOT)/40-runner-user.sh
registry:    ; @$(ROOT)/50-registry.sh
dotnet:      ; @$(ROOT)/70-dotnet.sh
logging:     ; @$(ROOT)/80-logging.sh

# The harness reads the provisioning library and the sandbox profile out of this tree, and its
# own default for that is a `FunctionSandbox` checkout beside itself - the layout from before
# this moved into blocks-logic. Tell it where the tree actually is rather than relying on that,
# so the harness needs no edit of its own and no second copy has to exist to satisfy it.
export SANDBOX_ROOT := $(ROOT)/..

verify:         ; @$(call need_harness) $(VERIFY)
verify-host:    ; @$(call need_harness) $(VERIFY) host
verify-sandbox: ; @$(call need_harness) $(VERIFY) sandbox
verify-network: ; @$(call need_harness) $(VERIFY) network

# A missing harness is an operator mistake, not a crash: say where it should be.
define need_harness
test -x $(VERIFY) || { \
  echo "verification harness not found at $(VERIFY)"; \
  echo "clone FunctionRunnerTests beside blocks-logic, or pass FUNCTION_RUNNER_TESTS=<path>"; \
  exit 1; };
endef

facts:         ; @cat $(ROOT)/.facts
firewall-show: ; @/usr/local/sbin/blocks-fn-firewall show

help:
	@echo "targets: preflight docker gvisor network runner-user registry dotnet"
	@echo "         logging verify verify-{host,sandbox,network}"
	@echo "         facts firewall-show"
