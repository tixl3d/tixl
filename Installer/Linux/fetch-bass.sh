#!/usr/bin/env bash
# Makes sure the BASS natives are in Dependencies/linux-x64.
#
# They are committed, so this normally finds them and does nothing. It exists to restore them if they
# are missing and, with --force, to pull a newer release from un4seen. Shipping them means accepting
# un4seen's terms; see Dependencies/linux-x64/README.md.
set -euo pipefail

force=false
if [[ "${1:-}" == "--force" ]]; then
    force=true
elif [[ -n "${1:-}" ]]; then
    echo "Usage: ${0##*/} [--force]" >&2
    exit 2
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
target="$script_dir/../../Dependencies/linux-x64"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

for name in bass bassmix bassflac; do
    if [[ -f "$target/lib$name.so" ]] && ! $force; then
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
