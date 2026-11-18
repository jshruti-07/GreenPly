#!/bin/bash
# Runs each service's db/init/*.sql in filename order.
# Mounted as /docker-entrypoint-initdb.d/00-init.sh
set -euo pipefail

for f in /pim-init/*/*.sql; do
  echo "Applying $f"

  # --single-transaction: the dev seed is ~2.4 MB of INSERTs generated from the
  #   client's catalogue export. In autocommit each statement is its own
  #   transaction with its own fsync, which took minutes; one transaction takes
  #   seconds. It also means a failure leaves NO partial seed behind.
  #
  # --quiet plus discarding stdout: psql otherwise streams the whole file into
  #   the container log, which was itself a large part of the cost and buried
  #   any real error under megabytes of echoed VALUES rows.
  #   Errors still reach stderr, and ON_ERROR_STOP aborts the run.
  psql \
    -v ON_ERROR_STOP=1 \
    --single-transaction \
    --quiet \
    -U "$POSTGRES_USER" \
    -d "$POSTGRES_DB" \
    -f "$f" > /dev/null

  echo "Applied  $f"
done

echo "db/init complete"
