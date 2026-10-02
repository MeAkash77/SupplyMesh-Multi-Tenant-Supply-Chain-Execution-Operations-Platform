#!/usr/bin/env bash
# First 5 minutes of an incident: what is unhealthy, why, and what changed?
set -uo pipefail
NS=supplymesh
kubectl get pods -n $NS -o wide
echo "--- not ready / restarting ---"; kubectl get pods -n $NS --field-selector=status.phase!=Running
echo "--- recent events ---"; kubectl get events -n $NS --sort-by=.lastTimestamp | tail -20
echo "--- OOMKilled? ---"; kubectl get pods -n $NS -o jsonpath='{range .items[*]}{.metadata.name}{"\t"}{.status.containerStatuses[0].lastState.terminated.reason}{"\n"}{end}'
echo "--- top ---"; kubectl top pods -n $NS
echo "--- last rollout ---"; kubectl rollout history deploy/orders-api -n $NS | tail -3
echo "--- errors in last 10m (orders-api) ---"; kubectl logs -n $NS deploy/orders-api --since=10m | grep -E '"@l":"(Error|Fatal)"|SLOW_REQUEST' | tail -20
