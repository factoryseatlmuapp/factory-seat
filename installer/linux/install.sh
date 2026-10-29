#!/bin/sh
# Installs Factory Seat for this user: the app in ~/.local/share/factory-seat, plus a menu entry
# and icon. No root needed. Run it from the unpacked folder:
#   ./install.sh              install or update
#   ./install.sh --uninstall  remove the app (your careers in ~/.config/LmuCareer stay)
set -e

here=$(cd "$(dirname "$0")" && pwd)
data=${XDG_DATA_HOME:-$HOME/.local/share}
app="$data/factory-seat"
desktop="$data/applications/factory-seat.desktop"
icon="$data/icons/hicolor/256x256/apps/factory-seat.png"

if [ "$1" = "--uninstall" ]; then
  rm -rf "$app"
  rm -f "$desktop" "$icon"
  echo "Factory Seat removed. Your careers are still in ~/.config/LmuCareer."
  exit 0
fi

mkdir -p "$app" "$(dirname "$desktop")" "$(dirname "$icon")"
cp "$here/factory-seat" "$here/Photino.Native.so" "$app/"
chmod +x "$app/factory-seat"
cp "$here/factory-seat.png" "$icon"
sed "s|@APP@|$app/factory-seat|" "$here/factory-seat.desktop" > "$desktop"
command -v update-desktop-database >/dev/null && update-desktop-database "$data/applications" 2>/dev/null || true

echo "Factory Seat installed. Find it in your applications menu, or run: $app/factory-seat"
if ! ldconfig -p 2>/dev/null | grep -q libwebkit2gtk-4.1; then
  echo
  echo "It needs WebKitGTK, which isn't installed. On Ubuntu, Mint or Debian:"
  echo "  sudo apt install libwebkit2gtk-4.1-0 gstreamer1.0-plugins-good"
fi
