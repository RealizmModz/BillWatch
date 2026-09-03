#!/bin/sh

set -eu

umask 077

: "${RESTIC_REPOSITORY:?RESTIC_REPOSITORY must be configured.}"
: "${RESTIC_PASSWORD:?RESTIC_PASSWORD must be configured.}"
: "${PGPASSWORD:?Database credentials must be configured.}"
: "${BILLWATCH_RELEASE_ID:?BILLWATCH_RELEASE_ID must be configured.}"

if [ "${#RESTIC_PASSWORD}" -lt 24 ] ||
   [ "$RESTIC_PASSWORD" = "replace-with-a-separate-long-random-backup-password" ]; then
    echo "RESTIC_PASSWORD must be a separate random value of at least 24 characters." >&2
    exit 64
fi

case "$RESTIC_REPOSITORY" in
    /*)
        if [ "${BILLWATCH_ALLOW_LOCAL_BACKUP_REPOSITORY:-false}" != true ]; then
            echo "Production backups require an off-host Restic repository." >&2
            exit 64
        fi
        ;;
esac

case "$BILLWATCH_RELEASE_ID" in
    replace-with-the-deployed-git-commit|*[!A-Za-z0-9._-]*|'')
        echo "BILLWATCH_RELEASE_ID must identify the deployed commit or version." >&2
        exit 64
        ;;
esac

backup_host="billwatch-production"
candidate_tag="billwatch-candidate"
complete_tag="billwatch-complete"
bundle_path="/work/bundle"
restore_path="/work/restore"
restore_database_host="${RESTORE_DATABASE_HOST:-restore-database}"

cleanup_work()
{
    rm -rf "$bundle_path" "$restore_path"
}

require_repository()
{
    if ! restic cat config >/dev/null 2>&1; then
        echo "The encrypted backup repository is unavailable or not initialized." >&2
        exit 1
    fi
}

initialize_repository()
{
    restic init
}

create_backup()
{
    require_repository
    cleanup_work
    mkdir -p "$bundle_path"

    key_file_count="$(find /source/data-protection -type f -name 'key-*.xml' -size +0c | wc -l | tr -d ' ')"

    if [ "$key_file_count" -lt 1 ]; then
        echo "The Data Protection key ring does not contain a non-empty key." >&2
        exit 1
    fi

    pg_dump \
        --host=database \
        --username=billwatch \
        --dbname=billwatch \
        --format=custom \
        --file="$bundle_path/database.dump"

    psql \
        --host=database \
        --username=billwatch \
        --dbname=billwatch \
        --no-psqlrc \
        --tuples-only \
        --no-align \
        --field-separator='|' \
        --command='SELECT "StorageKey", "SizeBytes" FROM "BillStatementUploads" ORDER BY "StorageKey";' \
        > "$bundle_path/statement-files.txt"

    upload_count="$(wc -l < "$bundle_path/statement-files.txt" | tr -d ' ')"

    if [ "${BILLWATCH_REQUIRE_RECOVERY_FIXTURE:-false}" = true ] &&
       [ "$upload_count" -lt 1 ]; then
        echo "The required recovery fixture is missing a statement upload." >&2
        exit 1
    fi

    tar -C /source/data-protection -cf "$bundle_path/data-protection.tar" .
    tar -C /source/statements -cf "$bundle_path/statements.tar" .

    latest_migration="$(psql --host=database --username=billwatch --dbname=billwatch --no-psqlrc --tuples-only --no-align --command='SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1;')"

    if [ -z "$latest_migration" ]; then
        echo "The production database has no migration history." >&2
        exit 1
    fi

    printf '%s\n' \
        'BillWatch encrypted production backup' \
        'FormatVersion=2' \
        "ReleaseId=$BILLWATCH_RELEASE_ID" \
        'PostgreSqlMajor=17' \
        "LatestMigration=$latest_migration" \
        "DataProtectionKeyFiles=$key_file_count" \
        "StatementUploads=$upload_count" \
        > "$bundle_path/manifest.txt"

    (
        cd "$bundle_path"
        sha256sum database.dump data-protection.tar statements.tar statement-files.txt manifest.txt > checksums.sha256
    )

    backup_output="/work/restic-backup.json"

    restic backup \
        --json \
        --host "$backup_host" \
        --tag "$candidate_tag" \
        "$bundle_path" \
        > "$backup_output"

    snapshot_id="$(jq -r 'select(.message_type == "summary") | .snapshot_id // empty' "$backup_output" | tail -n 1)"

    if [ -z "$snapshot_id" ]; then
        echo "Restic did not return a completed snapshot identifier." >&2
        exit 1
    fi

    restic check
    restic tag --add "$complete_tag" --remove "$candidate_tag" "$snapshot_id"

    cleanup_work
    rm -f "$backup_output"

    echo "Encrypted backup completed as snapshot $snapshot_id."
}

list_completed_snapshot()
{
    require_repository

    restic snapshots \
        --host "$backup_host" \
        --tag "$complete_tag" \
        --latest 1
}

verify_restore()
{
    require_repository
    cleanup_work
    mkdir -p "$restore_path"

    if ! pg_isready --host="$restore_database_host" --username=billwatch --dbname=postgres --timeout=5 >/dev/null 2>&1; then
        echo "The isolated restore database is not available." >&2
        exit 1
    fi

    snapshot_id="$(restic snapshots --json --host "$backup_host" --tag "$complete_tag" --latest 1 | jq -r '.[0].id // empty')"

    if [ -z "$snapshot_id" ]; then
        echo "No completed BillWatch backup snapshot exists." >&2
        exit 1
    fi

    restic restore "$snapshot_id" --target "$restore_path"

    restored_bundle="$restore_path/work/bundle"

    if [ ! -d "$restored_bundle" ]; then
        restored_bundle="$restore_path/bundle"
    fi

    if [ ! -d "$restored_bundle" ]; then
        echo "The restored snapshot does not contain the recovery bundle." >&2
        exit 1
    fi

    (
        cd "$restored_bundle"
        sha256sum -c checksums.sha256
    )

    grep -q '^FormatVersion=2$' "$restored_bundle/manifest.txt"
    grep -q '^PostgreSqlMajor=17$' "$restored_bundle/manifest.txt"
    pg_restore --list "$restored_bundle/database.dump" >/dev/null

    mkdir -p "$restore_path/extracted/data-protection" "$restore_path/extracted/statements"
    tar -C "$restore_path/extracted/data-protection" -xf "$restored_bundle/data-protection.tar"
    tar -C "$restore_path/extracted/statements" -xf "$restored_bundle/statements.tar"

    restored_key_count="$(find "$restore_path/extracted/data-protection" -type f -name 'key-*.xml' -size +0c | wc -l | tr -d ' ')"

    if [ "$restored_key_count" -lt 1 ]; then
        echo "The restored key ring is empty." >&2
        exit 1
    fi

    verification_database="billwatch_restore_verify"
    dropdb --host="$restore_database_host" --username=billwatch --if-exists "$verification_database" >/dev/null
    createdb --host="$restore_database_host" --username=billwatch "$verification_database"

    finish_verification()
    {
        exit_code="$?"
        trap - EXIT HUP INT TERM
        dropdb --host="$restore_database_host" --username=billwatch --if-exists "$verification_database" >/dev/null 2>&1 || true
        cleanup_work
        exit "$exit_code"
    }

    trap finish_verification EXIT
    trap 'exit 130' HUP INT TERM

    pg_restore \
        --host="$restore_database_host" \
        --username=billwatch \
        --dbname="$verification_database" \
        --exit-on-error \
        --no-owner \
        --no-privileges \
        "$restored_bundle/database.dump"

    migration_count="$(psql --host="$restore_database_host" --username=billwatch --dbname="$verification_database" --no-psqlrc --tuples-only --no-align --command='SELECT COUNT(*) FROM "__EFMigrationsHistory";')"

    if [ "$migration_count" -lt 1 ]; then
        echo "The restored database has no migration history." >&2
        exit 1
    fi

    database_upload_count="$(psql --host="$restore_database_host" --username=billwatch --dbname="$verification_database" --no-psqlrc --tuples-only --no-align --command='SELECT COUNT(*) FROM "BillStatementUploads";')"
    manifest_upload_count="$(wc -l < "$restored_bundle/statement-files.txt" | tr -d ' ')"

    if [ "$database_upload_count" -ne "$manifest_upload_count" ]; then
        echo "The restored statement manifest does not match the database." >&2
        exit 1
    fi

    while IFS='|' read -r storage_key expected_size
    do
        [ -n "$storage_key" ] || continue

        case "$storage_key" in
            /*|*..*|*\\*|*'|'*)
                echo "The restored database contains an unsafe statement storage key." >&2
                exit 1
                ;;
        esac

        restored_file="$restore_path/extracted/statements/$storage_key"

        if [ ! -f "$restored_file" ] ||
           [ "$(wc -c < "$restored_file" | tr -d ' ')" -ne "$expected_size" ]; then
            echo "A restored statement file is missing or has the wrong size." >&2
            exit 1
        fi
    done < "$restored_bundle/statement-files.txt"

    if [ "${BILLWATCH_REQUIRE_RECOVERY_FIXTURE:-false}" = true ] &&
       [ "$database_upload_count" -lt 1 ]; then
        echo "The isolated restore did not contain the required statement fixture." >&2
        exit 1
    fi

    dropdb --host="$restore_database_host" --username=billwatch "$verification_database"
    cleanup_work
    trap - EXIT HUP INT TERM

    echo "Encrypted backup restore verification passed for snapshot $snapshot_id."
}

trap 'cleanup_work; exit 130' HUP INT TERM

case "${1:-backup}" in
    init) initialize_repository ;;
    backup) create_backup ;;
    snapshot) list_completed_snapshot ;;
    verify) verify_restore ;;
    *)
        echo "Supported commands: init, backup, snapshot, verify." >&2
        exit 64
        ;;
esac
