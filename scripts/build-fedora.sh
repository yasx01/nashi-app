#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_command="${DOTNET_COMMAND:-dotnet}"
output="$root/build/fedora"
mkdir -p "$output/rpm/SOURCES"
# Recreate only this script's output folder so old assemblies cannot enter a new release.
rm -rf "$output/app"
"$dotnet_command" restore "$root/linux/Nashi.Linux" -r linux-x64 --locked-mode
"$dotnet_command" publish "$root/linux/Nashi.Linux" -c Release -r linux-x64 --self-contained true --no-restore -o "$output/app" -p:PublishSingleFile=false
cp "$root/LICENSE" "$output/app/LICENSE"
cp "$root/linux/README.md" "$output/app/README.md"
cp "$root/linux/FEATURES.md" "$root/linux/VERIFICATION.md" "$output/app/"
cp -r "$root/linux/screenshots" "$output/app/"
cp "$root/windows/THIRD-PARTY-NOTICES.txt" "$output/app/UPSTREAM-NOTICES.txt"
cp "$root/linux/THIRD-PARTY-NOTICES.txt" "$output/app/THIRD-PARTY-NOTICES.txt"
cp "$root/linux/nashi.desktop" "$output/app/"
cp "$root/Resources/Brand/NashiIcon.png" "$output/app/"
python3 "$root/scripts/package-linux-licenses.py" "$output/app"
tar -czf "$output/nashi-linux-x64.tar.gz" -C "$output/app" .
cp "$output/nashi-linux-x64.tar.gz" "$output/rpm/SOURCES/"
rpmbuild -bb --define "_topdir $output/rpm" "$root/linux/nashi.spec"
cp "$output"/rpm/RPMS/x86_64/nashi-*.rpm "$output/"
(cd "$output" && sha256sum nashi-linux-x64.tar.gz nashi-*.rpm > SHA256SUMS)
