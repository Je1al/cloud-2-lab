#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

if [[ -x "$HOME/.dotnet/dotnet" ]]; then
  export DOTNET_ROOT="$HOME/.dotnet"
  export PATH="$HOME/.dotnet:$PATH"
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "Не найден .NET SDK 8. Запусти: ./install-dotnet8-mac.sh"
  exit 1
fi

major="$(dotnet --version | cut -d. -f1)"
if [[ "$major" != "8" ]]; then
  echo "Нужен .NET SDK 8.x, обнаружена версия $(dotnet --version)."
  echo "Запусти установку встроенным скриптом: ./install-dotnet8-mac.sh"
  exit 1
fi

dotnet build SPS.sln --nologo
dotnet run --project SPS.Runner --no-build -- "$@"
