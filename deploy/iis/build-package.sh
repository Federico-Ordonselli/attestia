#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd -- "$script_dir/../.." && pwd)"
stage="$(mktemp -d "$script_dir/.package.XXXXXXXX")"
trap 'rm -rf -- "$stage"' EXIT
mkdir -p "$stage"/{api,admin,portale}

dotnet publish "$root/src/Attestia.Api" -c Release -r win-x64 --self-contained false -o "$stage/api"
if [[ -d "$root/web/admin" ]]; then
    (
        cd "$root/web/admin"
        npm ci
        npx ng build --base-href /admin/
        cp -a dist/admin/browser/. "$stage/admin/"
    )
else
    echo "Avviso: web/admin assente. Il pacchetto contiene solo la configurazione del back-office." >&2
fi

cp -a "$root/legacy/portale/." "$stage/portale/"
rm -f -- "$stage/portale/README.md"
for application in api admin portale; do
    cp -- "$script_dir/web.config.$application" "$stage/$application/web.config"
done

out="$script_dir/out"
mkdir -p "$out"
for application in api admin portale; do
    rm -rf -- "$out/$application"
    mv -- "$stage/$application" "$out/$application"
done
echo "Pacchetto IIS disponibile in $out"
