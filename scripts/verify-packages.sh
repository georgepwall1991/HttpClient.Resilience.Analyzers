#!/usr/bin/env bash
# Cross-platform pack smoke for discoverability assets (CI still uses Validate-Package.ps1).
set -euo pipefail

package_dir="${1:-artifacts/packages}"
repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"

version="$(dotnet msbuild src/HttpClient.Resilience.Analyzers.Package/HttpClient.Resilience.Analyzers.Package.csproj -getProperty:Version -nologo | tr -d '[:space:]')"
package="$package_dir/HttpClient.Resilience.Analyzers.$version.nupkg"

if [[ ! -f "$package" ]]; then
  echo "Package not found: $package" >&2
  exit 1
fi

cmp PACKAGE_README.md <(unzip -p "$package" README.md)

for asset in \
  assets/icon.png \
  assets/logo.png \
  assets/flow-ide-diagnostics.svg \
  assets/flow-before-after-fix.svg \
  assets/flow-product-loop.svg
do
  cmp "$asset" <(unzip -p "$package" "$asset")
done

nuspec="$(unzip -p "$package" HttpClient.Resilience.Analyzers.nuspec)"
for term in IHttpClientFactory HttpClient Compile-time PooledConnectionLifetime AddStandardResilienceHandler Roslyn Polly; do
  printf '%s' "$nuspec" | grep -Fq "$term" || {
    echo "Nuspec missing discoverability term: $term" >&2
    exit 1
  }
done

echo "Verified package README, assets, and discoverability metadata for $version."
