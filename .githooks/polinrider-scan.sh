#!/bin/sh
# Scans a commit's whole tree (default HEAD) for the PolinRider loader. Used by hypa-push and the pre-push hook.
# Copied from hypa-v2/.githooks; the only change is that Git LFS pointer files are allowed as fonts.
rev=${1:-HEAD}
git cat-file -e "$rev^{tree}" 2>/dev/null || { echo "::error::cannot read $rev"; exit 2; }
found=0
check() {
  if [ -n "$2" ]; then echo "::error::$1"; echo "$2"; found=1; fi
}
code() {
  git grep -nIE "$1" "$rev" -- '*.js' '*.cjs' '*.mjs' '*.jsx' '*.ts' '*.tsx' '*.mts' '*.cts' '*.json' '*.html' '*.sh' '*.py' ':!.githooks' | cut -c1-160
}
config() {
  git grep "$@" "$rev" -- '*.config.*' '.babelrc*' '*rc.js' '*rc.cjs' '*.js' '*.cjs' '*.mjs' | cut -c1-160
}

check "Code after a long run of spaces or tabs" "$(git grep -nIE '[ 	]{150,}[A-Za-z(]' "$rev" -- '*.js' '*.cjs' '*.mjs' '*.ts' '*.tsx' | cut -c1-160)"
check "Line over 1,000 characters in a config or .js file" "$(config -lIE '.{250}.{250}.{250}.{250}')"
check "Loader words in a config or .js file" "$(config -nIE 'createRequire|child_process|spawn\(|execSync')"
check "eval, new Function or global[...]" "$(code '(^|[^A-Za-z0-9_.])eval\(|new Function\(|global(This)?\[')"
check "Obfuscated code" "$(code '_0x[0-9a-fA-F]{4,}|parseInt\(0x|(\\x[0-9a-fA-F]{2}){20,}|(fromCharCode\([^)]*,){10,}')"
check "Ethereum RPC host, RPC call or wallet address" "$(code '1rpc\.io|drpc\.org|publicnode|blastapi|blockscout|infura\.io|alchemy\.com|etherscan|eth_(call|getTransaction)|0x[a-fA-F0-9]{40}([^a-fA-F0-9]|$)')"
check "Install script that downloads or runs inline code" "$(git grep -nE '"(pre|post)?install" *:.*(curl|wget|node -e|bash -c|https?:)' "$rev" -- '*package.json')"
check "Auto-run VS Code task" "$(git grep -nI -e folderOpen -e allowAutomaticTasks "$rev" -- '*.json' '.vscode' | cut -c1-160)"
check "Malware .gitignore entries" "$(git grep -nE 'temp_auto_push|temp_interactive_push|branch_structure\.json|config\.bat' "$rev" -- '*.gitignore')"
check "Planted loader or script file" "$(git ls-tree -r --name-only "$rev" | grep -iE '^api\.js$|(^|/)dist/setup\.js$|(^|/)tasks\.json$|\.(bat|cmd|ps1|exe|dll|scr|vbs|jar)$' | grep -vE '(^|/)gradlew\.bat$|(^|/)gradle-wrapper\.jar$')"
# Fonts in this repo live in Git LFS, so the committed file is a short text pointer ("version https://git-lfs..."). A pointer is
# expected; anything else that reads as text inside a font file is still flagged.
fonts=""
for f in $(git grep -lI . "$rev" -- '*.woff' '*.woff2' '*.ttf' '*.otf' '*.llf' | tr ' ' '?'); do
  path=$(printf '%s' "$f" | tr '?' ' ')
  if ! git show "$path" 2>/dev/null | head -c 60 | grep -q '^version https://git-lfs.github.com/spec/v1'; then fonts="$fonts$path
"; fi
done
check "Font file holding text or code" "$fonts"
exit $found
