#!/usr/bin/env bash
# Download the BASS natives into Dependencies/linux-x64 (not in git: un4seen's license forbids it).
# Shipping them in a release means accepting un4seen's terms, see Dependencies/linux-x64/README.md.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
target="$script_dir/../../Dependencies/linux-x64"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

for name in bass bassmix bassflac; do
    if [[ -f "$target/lib$name.so" ]]; then
        echo "lib$name.so already present, skipping."
        continue
    fi
    echo "Downloading ${name}24-linux.zip..."
    curl -fsSL -o "$work/$name.zip" "https://www.un4seen.com/files/${name}24-linux.zip"
    unzip -q -o "$work/$name.zip" -d "$work/$name"
    library="$(find "$work/$name" -name "lib$name.so" -path '*64*' -print -quit)"
    if [[ -z "$library" ]]; then
        echo "lib$name.so (x86-64) not found in ${name}24-linux.zip" >&2
        exit 1
    fi
    cp "$library" "$target/"
done
