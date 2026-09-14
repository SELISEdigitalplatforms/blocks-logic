#!/usr/bin/env bash
# 30-network.sh — the egress bridge for tenant sandboxes and the firewall that confines it.
#
# Two independent mechanisms:
#   1. a user-defined bridge `blocks-fn-egress` (172.29.0.0/24, no IPv6, inter-container
#      communication off) so sandboxes never share a network with infra containers;
#   2. an nftables table of our own, `inet blocks_fn`, whose base chains sit at
#      priority `filter - 10`, i.e. ahead of everything Docker, ufw or iptables-nft install
#      at priority 0. A `drop` there ends the packet no matter what a later table says, so
#      the policy holds whichever backend Docker uses and survives Docker rewriting its own
#      chains on every restart.
#
# What is blocked: from the sandbox subnet, every private, loopback, link-local, CGNAT and
# non-routable destination, the host's own address, and anything listed in
# /etc/blocks-runner/deny-cidrs — plus the IPv6 equivalents. All container-originated
# traffic to the host itself is dropped. Public internet egress stays open.
#
# DNS: Docker's embedded resolver at 127.0.0.11 is NOT usable from a gVisor sandbox. runsc
# with --network=sandbox gives the container its own netstack whose loopback lives inside
# the sentry, so nothing ever reaches the dockerd listener in the host netns (measured:
# "connection refused" on 127.0.0.11:53, while an external resolver answers normally). The
# runner therefore bind-mounts /etc/blocks-runner/resolv.conf over /etc/resolv.conf, which
# also keeps tenant lookups off the host resolver and off internal VPN DNS later on.
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_facts; load_versions

need_root
[ "${PREFLIGHT_OK:-no}" = yes ] || die "run ./00-preflight.sh first"
[ "${DOCKER_READY:-no}" = yes ] || die "run ./10-docker.sh first"
need_cmd nft

NET_NAME=blocks-fn-egress
NET_SUBNET=172.29.0.0/24
NET_GATEWAY=172.29.0.1
BRIDGE_IF=br-blocksfn
DENY_FILE=/etc/blocks-runner/deny-cidrs
DNS_FILE=/etc/blocks-runner/dns-servers
RESOLV_FILE=/etc/blocks-runner/resolv.conf
NFT_FILE=/etc/nftables.d/blocks-fn.nft
APPLY_BIN=/usr/local/sbin/blocks-fn-firewall
HOST_IP="${PRIMARY_IPV4:?preflight did not record PRIMARY_IPV4}"

# The host's own public segment is denied by default, not just the host address. A sandbox has
# no business reaching this VM's neighbours, and those neighbours are usually the operator's
# other machines. Set FN_ALLOW_HOST_SUBNET=1 to permit it — deliberately opt-in.
HOST_SUBNET="$(printf '%s' "$HOST_IP" | awk -F. '{print $1"."$2"."$3".0/24"}')"
if [ "${FN_ALLOW_HOST_SUBNET:-0}" = 1 ]; then
  warn "FN_ALLOW_HOST_SUBNET=1 — sandboxes may reach $HOST_SUBNET"
  HOST_SUBNET_ELEM="${HOST_IP}/32"
else
  HOST_SUBNET_ELEM="$HOST_SUBNET"
fi

apt_install nftables

# ------------------------------------------------------------------ bridge ----
step "Docker network $NET_NAME"
if docker network inspect "$NET_NAME" >/dev/null 2>&1; then
  CUR_SUBNET="$(docker network inspect "$NET_NAME" --format '{{(index .IPAM.Config 0).Subnet}}')"
  CUR_ICC="$(docker network inspect "$NET_NAME" --format '{{index .Options "com.docker.network.bridge.enable_icc"}}')"
  CUR_BR="$(docker network inspect "$NET_NAME" --format '{{index .Options "com.docker.network.bridge.name"}}')"
  if [ "$CUR_SUBNET" = "$NET_SUBNET" ] && [ "$CUR_ICC" = false ] && [ "$CUR_BR" = "$BRIDGE_IF" ]; then
    info "unchanged: $NET_NAME ($CUR_SUBNET, icc=$CUR_ICC, bridge=$CUR_BR)"
  else
    warn "$NET_NAME exists with a different configuration (subnet=$CUR_SUBNET icc=$CUR_ICC bridge=$CUR_BR)"
    if [ -n "$(docker network inspect "$NET_NAME" --format '{{range .Containers}}{{.Name}} {{end}}')" ]; then
      die "$NET_NAME has attached containers — remove them, then re-run to recreate the network"
    fi
    docker network rm "$NET_NAME" >/dev/null
    info "removed the mismatched network"
  fi
fi
if ! docker network inspect "$NET_NAME" >/dev/null 2>&1; then
  docker network create \
    --driver bridge \
    --subnet "$NET_SUBNET" \
    --gateway "$NET_GATEWAY" \
    --opt com.docker.network.bridge.name="$BRIDGE_IF" \
    --opt com.docker.network.bridge.enable_icc=false \
    --opt com.docker.network.bridge.enable_ip_masquerade=true \
    --opt com.docker.network.driver.mtu=1500 \
    "$NET_NAME" >/dev/null
  ok "created $NET_NAME ($NET_SUBNET, icc off, ipv6 off, bridge $BRIDGE_IF)"
fi
fact FN_NETWORK "$NET_NAME"; fact FN_SUBNET "$NET_SUBNET"; fact FN_BRIDGE "$BRIDGE_IF"

# ----------------------------------------------------------- extra denials ----
step "Deny list"
if [ ! -f "$DENY_FILE" ]; then
  write_file "$DENY_FILE" 0644 <<'DENY' || true
# Extra destinations sandboxes must not reach, one CIDR per line, IPv4 or IPv6.
# The mandatory ranges (RFC1918, loopback, link-local, CGNAT, the host address) are
# compiled in and need not be repeated. Add VPN and internal ranges here, then re-run
# provision/30-network.sh (or `make network`) to regenerate and reload the rules.
#
# The runner also reads this file as RUNNER_DENY_CIDRS.
#
# The host's own public /24 is already denied (set FN_ALLOW_HOST_SUBNET=1 to permit it).
# Add anything else a sandbox must not reach, e.g. a VPN range:
# 203.0.113.0/24
DENY
fi
EXTRA4=(); EXTRA6=()
while read -r line; do
  line="${line%%#*}"; line="$(printf '%s' "$line" | tr -d '[:space:]')"
  [ -z "$line" ] && continue
  case "$line" in
    *:*) EXTRA6+=("$line") ;;
    *)   EXTRA4+=("$line") ;;
  esac
done <"$DENY_FILE"
[ ${#EXTRA4[@]} -gt 0 ] && info "extra IPv4 denials: ${EXTRA4[*]}"
[ ${#EXTRA6[@]} -gt 0 ] && info "extra IPv6 denials: ${EXTRA6[*]}"
fact FN_DENY_EXTRA_V4 "${EXTRA4[*]:-}"
fact FN_DENY_EXTRA_V6 "${EXTRA6[*]:-}"

join_elems() { local IFS=', '; printf '%s' "$*"; }
DENY4_ELEMS="$(join_elems \
  10.0.0.0/8 172.16.0.0/12 192.168.0.0/16 169.254.0.0/16 100.64.0.0/10 127.0.0.0/8 \
  "$HOST_SUBNET_ELEM" \
  0.0.0.0/8 192.0.0.0/24 198.18.0.0/15 224.0.0.0/4 240.0.0.0/4 \
  "${EXTRA4[@]+"${EXTRA4[@]}"}")"
DENY6_ELEMS="$(join_elems \
  ::1/128 ::/128 fc00::/7 fe80::/10 ff00::/8 ::ffff:0.0.0.0/96 64:ff9b::/96 2002::/16 \
  "${EXTRA6[@]+"${EXTRA6[@]}"}")"

# -------------------------------------------------------------- rule table ----
step "nftables table inet blocks_fn"
write_file "$NFT_FILE" 0644 <<NFT || true
#!/usr/sbin/nft -f
# Generated by provision/30-network.sh — do not edit by hand.
# Edit ${DENY_FILE} and re-run that script instead.
#
# Base chains sit at priority (filter - 10) so they are evaluated before Docker's,
# ufw's and iptables-nft's chains at priority 0. A drop here is final.

table inet blocks_fn
delete table inet blocks_fn

table inet blocks_fn {
	set deny4 {
		type ipv4_addr
		flags interval
		elements = { ${DENY4_ELEMS} }
	}

	set deny6 {
		type ipv6_addr
		flags interval
		elements = { ${DENY6_ELEMS} }
	}

	chain forward {
		type filter hook forward priority filter - 10; policy accept;

		ct state established,related accept
		ct state invalid counter drop

		# Sandbox subnet may not reach private, loopback, link-local, CGNAT,
		# non-routable space, this host, or any operator-listed range.
		ip saddr ${NET_SUBNET} ip daddr @deny4 counter drop
		iifname "${BRIDGE_IF}" ip daddr @deny4 counter drop
		iifname "${BRIDGE_IF}" ip6 daddr @deny6 counter drop
		# The bridge carries no IPv6; anything that appears is not ours.
		iifname "${BRIDGE_IF}" meta nfproto ipv6 counter drop
		# Nothing may be routed back into the sandbox subnet from outside.
		oifname "${BRIDGE_IF}" ip saddr @deny4 counter drop
	}

	chain input {
		type filter hook input priority filter - 10; policy accept;

		ct state established,related accept
		# No sandbox may address the host itself on any port or protocol.
		# Docker's embedded DNS lives inside the container netns and is unaffected.
		iifname "${BRIDGE_IF}" counter drop
	}
}
NFT

write_file "$APPLY_BIN" 0755 <<'APPLY' || true
#!/usr/bin/env bash
# blocks-fn-firewall — (re)apply the sandbox egress policy.
# Run by blocks-fn-firewall.service, which is PartOf=docker.service so the table is
# reinstated whenever Docker restarts and rewrites its own chains.
set -euo pipefail
RULES=/etc/nftables.d/blocks-fn.nft
case "${1:-apply}" in
  apply)
    [ -f "$RULES" ] || { echo "blocks-fn-firewall: $RULES missing — refusing to run without a policy" >&2; exit 1; }
    /usr/sbin/nft -f "$RULES"
    ;;
  show)   /usr/sbin/nft list table inet blocks_fn ;;
  delete) /usr/sbin/nft delete table inet blocks_fn 2>/dev/null || true ;;
  *) echo "usage: blocks-fn-firewall [apply|show|delete]" >&2; exit 2 ;;
esac
APPLY

write_file /etc/systemd/system/blocks-fn-firewall.service 0644 <<'UNIT' || true
[Unit]
Description=Blocks Functions sandbox egress firewall
Documentation=file:/etc/nftables.d/blocks-fn.nft
After=docker.service network-pre.target nftables.service
Wants=network-pre.target
PartOf=docker.service

[Service]
Type=oneshot
RemainAfterExit=yes
ExecStart=/usr/local/sbin/blocks-fn-firewall apply
ExecReload=/usr/local/sbin/blocks-fn-firewall apply
ExecStop=/usr/local/sbin/blocks-fn-firewall delete

[Install]
WantedBy=multi-user.target
WantedBy=docker.service
UNIT

systemctl daemon-reload
systemctl enable blocks-fn-firewall.service >/dev/null 2>&1 || die "could not enable blocks-fn-firewall.service"
systemctl restart blocks-fn-firewall.service || die "could not apply the firewall rules"
systemctl is-active --quiet blocks-fn-firewall.service || die "blocks-fn-firewall.service is not active"
ok "blocks-fn-firewall.service active (reapplies with docker.service)"

# ------------------------------------------------------------ verification ----
step "Verification"
nft list table inet blocks_fn >/dev/null 2>&1 || die "table inet blocks_fn is not loaded"
for chain in forward input; do
  CHAIN_TXT="$(nft -a list chain inet blocks_fn "$chain")"
  grep -q 'priority filter - 10' <<<"$CHAIN_TXT" \
    || die "chain $chain is not at priority filter - 10"
done
ok "table inet blocks_fn loaded, both hooks at priority filter - 10"
COUNT4="$(nft -j list set inet blocks_fn deny4 | jq '[.nftables[]|select(.set)|.set.elem[]]|length')"
COUNT6="$(nft -j list set inet blocks_fn deny6 | jq '[.nftables[]|select(.set)|.set.elem[]]|length')"
ok "deny sets loaded: $COUNT4 IPv4 ranges, $COUNT6 IPv6 ranges"
fact FN_DENY_COUNT_V4 "$COUNT4"; fact FN_DENY_COUNT_V6 "$COUNT6"

# ----------------------------------------------------------------- sandbox DNS ----
step "Sandbox DNS"
if [ ! -f "$DNS_FILE" ]; then
  write_file "$DNS_FILE" 0644 <<'DNS' || true
# Resolvers handed to tenant sandboxes, one address per line, most preferred first.
# They must be reachable through the egress policy, so public resolvers only — anything
# inside a denied range is refused below. Re-run provision/30-network.sh after editing.
1.1.1.1
1.0.0.1
8.8.8.8
DNS
fi
DNS_SERVERS=()
while read -r line; do
  line="${line%%#*}"; line="$(printf '%s' "$line" | tr -d '[:space:]')"
  [ -z "$line" ] && continue
  DNS_SERVERS+=("$line")
done <"$DNS_FILE"
[ ${#DNS_SERVERS[@]} -gt 0 ] || die "$DNS_FILE lists no resolvers"
for d in "${DNS_SERVERS[@]}"; do
  if nft get element inet blocks_fn deny4 "{ $d }" >/dev/null 2>&1 \
     || nft get element inet blocks_fn deny6 "{ $d }" >/dev/null 2>&1; then
    die "resolver $d falls inside a denied range — sandboxes could never reach it"
  fi
done
{
  echo "# Generated by provision/30-network.sh — bind-mounted read-only over /etc/resolv.conf"
  echo "# in every sandbox. Docker's embedded resolver is unreachable under gVisor."
  for d in "${DNS_SERVERS[@]}"; do echo "nameserver $d"; done
  echo "options ndots:0 timeout:2 attempts:2"
} | write_file "$RESOLV_FILE" 0644 || true
fact FN_RESOLV_CONF "$RESOLV_FILE"
fact FN_DNS_SERVERS "${DNS_SERVERS[*]}"
ok "sandbox resolvers: ${DNS_SERVERS[*]}"

ip link show "$BRIDGE_IF" >/dev/null 2>&1 || warn "$BRIDGE_IF not up yet — Docker creates it with the first container"
fact NETWORK_READY yes
ok "network ready"
