#!/usr/bin/env bash
# Shared by the sl4n hooks. Not a hook itself.
set -euo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT"

# The projects target net8.0. On a machine that only has a newer runtime installed, anything that
# EXECUTES (tests, publish, run) fails with "framework not found" — a toolchain mismatch, not a
# broken change. Rolling forward makes the hook work on both. CI installs 8.0.x and ignores this.
export DOTNET_ROLL_FORWARD="${DOTNET_ROLL_FORWARD:-LatestMajor}"

step() { printf '\033[1;34m▸ %s\033[0m\n' "$1"; }
die()  { printf '\033[1;31m✗ %s\033[0m\n' "$1" >&2; exit 1; }
ok()   { printf '\033[1;32m✓ %s\033[0m\n' "$1"; }

require_dotnet() {
  command -v dotnet >/dev/null 2>&1 || die "dotnet not on PATH — cannot verify this change."
}

gate_build() {
  step "build (Release)"
  # src/ builds with TreatWarningsAsErrors, so a missing doc or an AOT/trim warning fails here.
  dotnet build sl4n.slnx -c Release --nologo -v quiet >/dev/null \
    || die "build failed. Run: dotnet build sl4n.slnx -c Release"
  ok "build clean"
}

gate_tests() {
  step "tests"
  dotnet test tests/sl4n.Tests/sl4n.Tests.csproj --no-build -c Release --nologo -v quiet >/dev/null \
    || die "tests failed. Run: dotnet test tests/sl4n.Tests/sl4n.Tests.csproj -c Release"
  ok "tests green"
}

gate_aot_smoke() {
  step "NativeAOT smoke (publish + execute)"
  local rid out
  case "$(uname -s)/$(uname -m)" in
    Darwin/arm64) rid=osx-arm64  ;;
    Darwin/x86_64) rid=osx-x64   ;;
    Linux/aarch64) rid=linux-arm64 ;;
    Linux/x86_64) rid=linux-x64  ;;
    *) printf '  skipped: unknown platform %s\n' "$(uname -s)/$(uname -m)"; return 0 ;;
  esac
  out="$(mktemp -d)"
  trap 'rm -rf "$out"' RETURN

  dotnet publish tests/sl4n.AotSmoke/sl4n.AotSmoke.csproj -c Release -r "$rid" -o "$out" --nologo -v quiet >/dev/null \
    || die "AOT publish failed for $rid. Compiling is the claim; this is the proof."
  "$out/sl4n.AotSmoke" >/dev/null \
    || die "the AOT binary ran and FAILED its own assertions — masking or keyed DI broke under AOT."
  ok "AOT smoke passed ($rid)"
}
