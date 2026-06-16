#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SOURCE_DIR="$ROOT_DIR/src/SupplierIntelligence.Web"
DEV_DIR="${SUPPLIER_WEB_DEV_DIR:-/tmp/supplier-intelligence-web-dev}"
API_TARGET="${VITE_API_TARGET:-http://127.0.0.1:5142}"

if ! command -v npm >/dev/null 2>&1; then
  echo "npm is required to run the frontend dev server." >&2
  exit 1
fi

mkdir -p "$DEV_DIR"

rsync -a --delete \
  --exclude node_modules \
  --exclude dist \
  --exclude '*.tsbuildinfo' \
  "$SOURCE_DIR/" "$DEV_DIR/"

cd "$DEV_DIR"

if [ ! -d node_modules ]; then
  npm install
fi

echo "Frontend source: $SOURCE_DIR"
echo "Dev server copy: $DEV_DIR"
echo "API target:       $API_TARGET"
echo "Frontend URL:     http://127.0.0.1:5174/"

VITE_API_TARGET="$API_TARGET" npm run dev
