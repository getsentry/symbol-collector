#!/usr/bin/env bash
# Redirect stderr to stdout to avoid weird Powershell errors
exec 2>&1
set -x

export PATH=./node_modules/.bin:$PATH

upload_artifacts() {
  # To be kept in sync with appveyor configs
  zeus upload -t "application/zip+apk" ./src/SymbolCollector.Android/bin/release/*Signed.apk
  zeus upload -t "application/zip" ./src/SymbolCollector.Console/osx-x64.zip
  zeus upload -t "application/zip" ./src/SymbolCollector.Console/linux-x64.zip
  zeus upload -t "application/zip" ./src/SymbolCollector.Console/linux-musl-x64.zip
  zeus upload -t "application/zip" ./src/SymbolCollector.Console/linux-arm.zip
  zeus upload -t "application/zip" ./src/SymbolCollector.Server/server.zip
  zeus job update --status=passed
}

report_pending() {
  zeus job update --status=pending
}


report_failed() {
  zeus job update --status=failed
}

check_branch() {
  # Ignore errors if not on release branch
  if [[ ! "${APPVEYOR_REPO_BRANCH:-}" =~ ^release/ ]]; then
    trap - EXIT
    echo "Not on a release branch, ignoring all errors, if any."
    exit 0
  fi
}

trap check_branch EXIT

command="${1:-}"
if [[ "$command" == "upload_artifacts" ]]; then
  upload_artifacts
elif [[ "$command" == "report_pending" ]]; then
  report_pending
elif [[ "$command" == "report_failed" ]]; then
  report_failed
else
  echo "Invalid command"
  exit 1
fi
