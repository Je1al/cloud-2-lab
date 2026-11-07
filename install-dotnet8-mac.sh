#!/usr/bin/env bash
set -euo pipefail

INSTALL_DIR="${DOTNET_INSTALL_DIR:-$HOME/.dotnet}"
mkdir -p "$INSTALL_DIR"

if ! command -v curl >/dev/null 2>&1; then
  echo "Не найден curl. Установи Xcode Command Line Tools командой: xcode-select --install"
  exit 1
fi

echo "Устанавливаю .NET SDK 8 в $INSTALL_DIR..."
curl -fsSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin \
  --channel 8.0 \
  --install-dir "$INSTALL_DIR" \
  --no-path

export DOTNET_ROOT="$INSTALL_DIR"
export PATH="$INSTALL_DIR:$PATH"

PROFILE="$HOME/.zshrc"
PATH_LINE="export PATH=\"$INSTALL_DIR:\$PATH\""
touch "$PROFILE"
if ! grep -Fq "$PATH_LINE" "$PROFILE"; then
  printf '\n# .NET 8 installed by SPSPublic\n%s\n' "$PATH_LINE" >> "$PROFILE"
fi

echo
echo "Установлена версия: $(dotnet --version)"
echo
echo "Теперь запусти: ./run-mac.sh --seed 42"
echo
echo "Настройка сохранена в $PROFILE. Для текущего окна выполни: source $PROFILE"
