#!/usr/bin/env bash
# Consumer lag + DLQ depth. Usage: ./kafka-lag.sh [bootstrap]
set -euo pipefail
BS="${1:-localhost:9092}"
docker exec kafka kafka-consumer-groups --bootstrap-server "$BS" --describe --group inventory-worker
echo "--- DLQ end offsets ---"
docker exec kafka kafka-run-class kafka.tools.GetOffsetShell --bootstrap-server "$BS" --topic po.events.dlq
