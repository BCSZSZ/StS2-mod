#!/usr/bin/env bash
# Runs ON the EC2 box (Ubuntu 24.04), as user-data or by hand.
# Installs the .NET 8 SDK + tools, clones the repo, and builds the Tools CLI in
# Release. It does NOT copy card_facts.generated.json (git-ignored) - scp that up
# separately (see README Step 2). Safe to re-run.
set -euo pipefail

REPO_URL="${REPO_URL:-https://github.com/BCSZSZ/StS2-mod.git}"
REPO_DIR="${REPO_DIR:-$HOME/StS2-mod}"
DOTNET_DIR="${DOTNET_DIR:-/opt/dotnet}"
SKIP_CLONE="${SKIP_CLONE:-0}"

echo "== base packages (apt or dnf) =="
# libicu is needed by the .NET runtime; awscli v2 is preinstalled on Amazon Linux 2023.
if command -v apt-get >/dev/null 2>&1; then
  sudo apt-get update -y
  sudo DEBIAN_FRONTEND=noninteractive apt-get install -y git curl tmux unzip ca-certificates libicu-dev
elif command -v dnf >/dev/null 2>&1; then
  sudo dnf install -y git curl tmux unzip ca-certificates libicu
else
  echo "no supported package manager (apt-get/dnf) found"; exit 1
fi

echo "== awscli v2 (skip if preinstalled) =="
if ! command -v aws >/dev/null 2>&1; then
  curl -sSL "https://awscli.amazonaws.com/awscli-exe-linux-x86_64.zip" -o /tmp/awscliv2.zip
  unzip -q -o /tmp/awscliv2.zip -d /tmp
  sudo /tmp/aws/install --update
fi

echo "== .NET 8 SDK (distro-agnostic install script) =="
if [ ! -x "$DOTNET_DIR/dotnet" ]; then
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  sudo bash /tmp/dotnet-install.sh --channel 8.0 --install-dir "$DOTNET_DIR"
fi
# Make dotnet available in this shell and future login shells.
export PATH="$PATH:$DOTNET_DIR"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
if ! grep -q "$DOTNET_DIR" "$HOME/.bashrc" 2>/dev/null; then
  {
    echo "export PATH=\$PATH:$DOTNET_DIR"
    echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
    echo "export DOTNET_NOLOGO=1"
  } >> "$HOME/.bashrc"
fi
"$DOTNET_DIR/dotnet" --info | head -5

echo "== repo =="
if [ "$SKIP_CLONE" != "1" ]; then
  if [ ! -d "$REPO_DIR/.git" ]; then
    git clone --depth 1 "$REPO_URL" "$REPO_DIR"
  else
    git -C "$REPO_DIR" pull --ff-only || true
  fi
fi

echo "== build Tools (Release) =="
cd "$REPO_DIR"
"$DOTNET_DIR/dotnet" build CardValueOverlay.Tools/CardValueOverlay.Tools.csproj -c Release -v minimal

echo
echo "Bootstrap complete."
echo "Next: scp card_facts.generated.json into $REPO_DIR/data/extracted/ then run:"
echo "  cd $REPO_DIR && bash ops/aws-search-policy/run-collection.sh"
