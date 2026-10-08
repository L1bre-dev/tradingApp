#!/usr/bin/env bash
# Nightly database backup: a compressed SQL dump in ./backups, kept 14 days.
# Install with cron (see README.md). Restore steps are in README.md too.
set -euo pipefail
cd "$(dirname "$0")"

mkdir -p backups
file="backups/journal-$(date -u +%Y%m%d-%H%M%S).sql.gz"

# If the dump fails, delete the incomplete file so it is never mistaken for a good backup.
trap 'rm -f "$file"' ERR
docker compose exec -T postgres pg_dump -U journal -d journal | gzip > "$file"
trap - ERR

find backups -name 'journal-*.sql.gz' -mtime +14 -delete
echo "Backup written: $file ($(du -h "$file" | cut -f1))"
