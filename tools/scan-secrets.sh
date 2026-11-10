#!/usr/bin/env bash
# Scan the working tree and git history for secrets before every push. Public repo: no keys, ever.
# Uses gitleaks if installed, else a conservative grep fallback. Exit 1 on any hit.
set -u
cd "$(git rev-parse --show-toplevel)"

if command -v gitleaks >/dev/null 2>&1; then
  gitleaks detect --source . --no-banner --redact && gitleaks protect --staged --no-banner --redact
  exit $?
fi

echo "gitleaks not found; using grep fallback (install gitleaks for a real scan: brew install gitleaks)"
PATTERN='(x-api-key|api[_-]?key|secret|token|passwd|password|bearer|authorization)["'"'"']?[[:space:]]*[:=][[:space:]]*["'"'"'][A-Za-z0-9_\-\.]{16,}["'"'"']|AKIA[0-9A-Z]{16}|AIza[0-9A-Za-z_\-]{35}|-----BEGIN [A-Z ]*PRIVATE KEY-----|sk-[A-Za-z0-9]{20,}|ghp_[A-Za-z0-9]{30,}|xox[baprs]-[A-Za-z0-9-]{10,}|OC[A-Za-z0-9]{20,}\|[A-Za-z0-9]{20,}'
fail=0
# working tree (tracked + untracked, minus ignored) and the staged/committed content
files=$(git ls-files -co --exclude-standard)
if [ -n "$files" ] && echo "$files" | xargs grep -IniE "$PATTERN" 2>/dev/null | grep -v 'tools/scan-secrets.sh'; then fail=1; fi
if git rev-parse HEAD >/dev/null 2>&1; then
  if git log -p --all 2>/dev/null | grep -niE "^\+.*($PATTERN)" | grep -v 'tools/scan-secrets.sh'; then fail=1; fi
fi
# forbidden file types
if echo "$files" | grep -E '\.(keystore|jks|p12|apk|aab|env)$|(^|/)\.env' ; then fail=1; fi
[ $fail -eq 0 ] && echo "secret scan: clean" || echo "secret scan: FOUND POSSIBLE SECRETS, do not push"
exit $fail
