#!/usr/bin/env bash
set -euo pipefail
export LC_ALL=C

CONTEXT="${AKRON_AUTORESEARCH_CONTEXT:-home-k8s}"
NAMESPACE="${AKRON_AUTORESEARCH_NAMESPACE:-sandboxes}"
POD="${AKRON_AUTORESEARCH_POD:-akron-autoresearch}"
MANIFEST="scripts/akron-corpus/autoresearch-k8s.yaml"
REMOTE_ROOT="/data/autoresearch"
REMOTE_WORKTREE="${REMOTE_ROOT}/worktree"
REMOTE_INCOMING="${REMOTE_ROOT}/incoming"
REMOTE_CACHE="${REMOTE_ROOT}/build-cache"
REMOTE_RESULT="${REMOTE_ROOT}/result.txt"
REMOTE_LOCK="${REMOTE_ROOT}/run.lock"
LOCAL_BASELINE="${AKRON_AUTORESEARCH_BASELINE:-.tmp-akron-autoresearch-baseline.txt}"
LOCK_HOST="${HOSTNAME:-$(hostname)}"
LOCK_OWNER="${LOCK_HOST}:$$"

if [ ! -f "$MANIFEST" ]; then
    echo "error: missing sandbox manifest: $MANIFEST" >&2
    exit 1
fi

# Transport and tiny ratio arithmetic happen locally; restore, compilation and
# the complete benchmark workload run in the resource-isolated sandbox.
kubectl --context "$CONTEXT" -n "$NAMESPACE" apply -f "$MANIFEST" >/dev/null
kubectl --context "$CONTEXT" -n "$NAMESPACE" wait --for=condition=Ready "pod/${POD}" --timeout=180s >/dev/null

if ! kubectl --context "$CONTEXT" -n "$NAMESPACE" exec "$POD" -- mkdir "$REMOTE_LOCK" 2>/dev/null; then
    existing_owner="$(kubectl --context "$CONTEXT" -n "$NAMESPACE" exec "$POD" -- cat "${REMOTE_LOCK}/owner" 2>/dev/null || true)"
    existing_host="${existing_owner%%:*}"
    existing_pid="${existing_owner#*:}"
    if [ "$existing_host" = "$LOCK_HOST" ] &&
       [[ "$existing_pid" =~ ^[0-9]+$ ]] &&
       ! kill -0 "$existing_pid" 2>/dev/null; then
        # The invoking shell died (including a kubectl/container SIGKILL) before
        # its EXIT trap could reconnect to remove the PVC-backed lock.
        kubectl --context "$CONTEXT" -n "$NAMESPACE" exec "$POD" -- rm -rf "$REMOTE_LOCK"
        kubectl --context "$CONTEXT" -n "$NAMESPACE" exec "$POD" -- mkdir "$REMOTE_LOCK"
    else
        echo "error: another Akron autoresearch grader is already running (owner ${existing_owner:-unknown})" >&2
        exit 1
    fi
fi
printf '%s\n' "$LOCK_OWNER" | kubectl --context "$CONTEXT" -n "$NAMESPACE" exec -i "$POD" -- \
    sh -c "cat > '${REMOTE_LOCK}/owner'"
cleanup_lock() {
    kubectl --context "$CONTEXT" -n "$NAMESPACE" exec "$POD" -- rm -rf "$REMOTE_LOCK" >/dev/null 2>&1 || true
}
trap cleanup_lock EXIT INT TERM

kubectl --context "$CONTEXT" -n "$NAMESPACE" exec "$POD" -- \
    sh -c "rm -rf '${REMOTE_INCOMING}' && mkdir -p '${REMOTE_INCOMING}' '${REMOTE_CACHE}'"

tar -cf - \
    --exclude='bin' \
    --exclude='obj' \
    --exclude='*.user' \
    Source tests licenses \
    Akron.sln Directory.Build.props global.json everest.yaml LICENSE \
    | kubectl --context "$CONTEXT" -n "$NAMESPACE" exec -i "$POD" -- \
        tar -xf - -C "$REMOTE_INCOMING"

# Replace the source tree rather than overlaying it, so deleting a candidate
# source file cannot leave a stale copy in the grader. Preserve only NuGet's
# generated restore state; package/project files are outside the optimization
# scope and the fixed path keeps their absolute references valid.
kubectl --context "$CONTEXT" -n "$NAMESPACE" exec "$POD" -- sh -c "
    set -eu
    rm -rf '${REMOTE_CACHE}/Source.obj' '${REMOTE_CACHE}/tests.obj'
    if [ -d '${REMOTE_WORKTREE}/Source/obj' ]; then
        mv '${REMOTE_WORKTREE}/Source/obj' '${REMOTE_CACHE}/Source.obj'
    fi
    if [ -d '${REMOTE_WORKTREE}/tests/obj' ]; then
        mv '${REMOTE_WORKTREE}/tests/obj' '${REMOTE_CACHE}/tests.obj'
    fi
    rm -rf '${REMOTE_WORKTREE}'
    mv '${REMOTE_INCOMING}' '${REMOTE_WORKTREE}'
    ln -s /data/game/Celeste '${REMOTE_WORKTREE}/lib-stripped'
    if [ -d '${REMOTE_CACHE}/Source.obj' ]; then
        mv '${REMOTE_CACHE}/Source.obj' '${REMOTE_WORKTREE}/Source/obj'
    fi
    if [ -d '${REMOTE_CACHE}/tests.obj' ]; then
        mv '${REMOTE_CACHE}/tests.obj' '${REMOTE_WORKTREE}/tests/obj'
    fi
"

# Bootstrap the locked NuGet graph once into the persistent PVC. Measured runs
# use --no-restore, so package resolution and any registry traffic are outside
# the workload and do not recur between candidates.
if ! kubectl --context "$CONTEXT" -n "$NAMESPACE" exec "$POD" -- \
    test -s "${REMOTE_WORKTREE}/tests/obj/project.assets.json"; then
    kubectl --context "$CONTEXT" -n "$NAMESPACE" exec "$POD" -- \
        dotnet restore "${REMOTE_WORKTREE}/tests/akron-tests.csproj" --locked-mode --nologo
fi

kubectl --context "$CONTEXT" -n "$NAMESPACE" exec "$POD" -- rm -f "$REMOTE_RESULT"
kubectl --context "$CONTEXT" -n "$NAMESPACE" exec "$POD" -- env \
    AKRON_AUTORESEARCH_CORPUS_ROOT=/data \
    AKRON_AUTORESEARCH_RESULT="$REMOTE_RESULT" \
    DOTNET_TieredCompilation=0 \
    DOTNET_ReadyToRun=0 \
    dotnet test "${REMOTE_WORKTREE}/tests/akron-tests.csproj" \
        --configuration Release \
        --nologo \
        --no-restore \
        --filter 'FullyQualifiedName~AutoresearchCorpusBenchmark' \
        --logger 'console;verbosity=minimal'

if ! kubectl --context "$CONTEXT" -n "$NAMESPACE" exec "$POD" -- test -s "$REMOTE_RESULT"; then
    echo "error: benchmark did not produce $REMOTE_RESULT" >&2
    exit 1
fi

result_text="$(kubectl --context "$CONTEXT" -n "$NAMESPACE" exec "$POD" -- cat "$REMOTE_RESULT")"
if [ "${AKRON_AUTORESEARCH_RECALIBRATE:-0}" = "1" ]; then
    baseline_tmp="${LOCAL_BASELINE}.tmp"
    printf '%s\n' "$result_text" > "$baseline_tmp"
    mv "$baseline_tmp" "$LOCAL_BASELINE"
elif [ ! -s "$LOCAL_BASELINE" ]; then
    echo "error: no calibrated baseline at $LOCAL_BASELINE; run the clean baseline once with AKRON_AUTORESEARCH_RECALIBRATE=1" >&2
    exit 1
fi
baseline_text="$(cat "$LOCAL_BASELINE")"

required_keys=(
    snapshot_count source_compressed_bytes candidate_compressed_bytes decompressed_bytes
    corpus_allocated_bytes synthetic_allocated_bytes vanilla_allocated_bytes
    cookie_allocated_bytes hyperlife_allocated_bytes dsides_allocated_bytes
    working_ms cpu_ms peak_working_set_bytes
)

declare -A current baseline
parse_metrics() {
    local text="$1"
    local target_name="$2"
    local -n target="$target_name"
    local key value
    while IFS='=' read -r key value; do
        [ -n "$key" ] || continue
        case "$key" in
            snapshot_count|source_compressed_bytes|candidate_compressed_bytes|decompressed_bytes|corpus_allocated_bytes|synthetic_allocated_bytes|vanilla_allocated_bytes|cookie_allocated_bytes|hyperlife_allocated_bytes|dsides_allocated_bytes|working_ms|cpu_ms|peak_working_set_bytes) ;;
            *) echo "error: unexpected benchmark metric: $key" >&2; exit 1 ;;
        esac
        if [[ ! "$value" =~ ^[0-9]+([.][0-9]+)?$ ]]; then
            echo "error: non-numeric benchmark metric: $key=$value" >&2
            exit 1
        fi
        target["$key"]="$value"
    done <<< "$text"

    for key in "${required_keys[@]}"; do
        if [ -z "${target[$key]:-}" ]; then
            echo "error: missing benchmark metric: $key" >&2
            exit 1
        fi
        if ! awk -v value="${target[$key]}" 'BEGIN { exit !(value > 0) }'; then
            echo "error: benchmark metric must be positive: $key=${target[$key]}" >&2
            exit 1
        fi
    done
}

parse_metrics "$result_text" current
parse_metrics "$baseline_text" baseline

if [ "${current[snapshot_count]}" != "23" ] || [ "${baseline[snapshot_count]}" != "23" ]; then
    echo "error: fixed workload must contain exactly 23 snapshots" >&2
    exit 1
fi
if [ "${current[decompressed_bytes]}" != "${baseline[decompressed_bytes]}" ] || \
   [ "${current[source_compressed_bytes]}" != "${baseline[source_compressed_bytes]}" ]; then
    echo "error: corpus identity differs from the calibrated baseline" >&2
    exit 1
fi

ratio() {
    awk -v numerator="$1" -v denominator="$2" \
        'BEGIN { if (!(numerator >= 0) || !(denominator > 0)) exit 1; printf "%.9f", numerator / denominator }'
}

corpus_allocation_ratio="$(ratio "${current[corpus_allocated_bytes]}" "${baseline[corpus_allocated_bytes]}")"
synthetic_allocation_ratio="$(ratio "${current[synthetic_allocated_bytes]}" "${baseline[synthetic_allocated_bytes]}")"
allocation_ratio="$(awk -v corpus="$corpus_allocation_ratio" -v synthetic="$synthetic_allocation_ratio" \
    'BEGIN { printf "%.9f", 0.9 * corpus + 0.1 * synthetic }')"
size_ratio="$(ratio "${current[candidate_compressed_bytes]}" "${baseline[candidate_compressed_bytes]}")"
vanilla_allocation_ratio="$(ratio "${current[vanilla_allocated_bytes]}" "${baseline[vanilla_allocated_bytes]}")"
cookie_allocation_ratio="$(ratio "${current[cookie_allocated_bytes]}" "${baseline[cookie_allocated_bytes]}")"
hyperlife_allocation_ratio="$(ratio "${current[hyperlife_allocated_bytes]}" "${baseline[hyperlife_allocated_bytes]}")"
dsides_allocation_ratio="$(ratio "${current[dsides_allocated_bytes]}" "${baseline[dsides_allocated_bytes]}")"
worst_cohort_allocation_ratio="$(awk \
    -v a="$vanilla_allocation_ratio" -v b="$cookie_allocation_ratio" \
    -v c="$hyperlife_allocation_ratio" -v d="$dsides_allocation_ratio" \
    'BEGIN { max=a; if (b>max) max=b; if (c>max) max=c; if (d>max) max=d; printf "%.9f", max }')"
working_ratio="$(ratio "${current[working_ms]}" "${baseline[working_ms]}")"
cpu_ratio="$(ratio "${current[cpu_ms]}" "${baseline[cpu_ms]}")"
peak_working_set_ratio="$(ratio "${current[peak_working_set_bytes]}" "${baseline[peak_working_set_bytes]}")"
overall_cost="$(awk -v allocation="$allocation_ratio" -v size="$size_ratio" \
    'BEGIN { printf "%.9f", 0.75 * allocation + 0.25 * size }')"

# Correctness already hard-fails in xUnit. These prevent the scalar average from
# buying a large regression in one important dimension with a win in another.
if ! awk -v value="$worst_cohort_allocation_ratio" 'BEGIN { exit !(value <= 1.05) }'; then
    echo "error: a workload cohort regressed allocation by more than 5%" >&2
    exit 1
fi
if ! awk -v value="$synthetic_allocation_ratio" 'BEGIN { exit !(value <= 1.05) }'; then
    echo "error: the synthetic capture workload regressed allocation by more than 5%" >&2
    exit 1
fi
if ! awk -v value="$size_ratio" 'BEGIN { exit !(value <= 1.01) }'; then
    echo "error: compressed corpus size regressed by more than 1%" >&2
    exit 1
fi

printf 'METRIC overall_cost=%s\n' "$overall_cost"
printf 'METRIC allocation_ratio=%s\n' "$allocation_ratio"
printf 'METRIC corpus_allocation_ratio=%s\n' "$corpus_allocation_ratio"
printf 'METRIC synthetic_allocation_ratio=%s\n' "$synthetic_allocation_ratio"
printf 'METRIC size_ratio=%s\n' "$size_ratio"
printf 'METRIC worst_cohort_allocation_ratio=%s\n' "$worst_cohort_allocation_ratio"
printf 'METRIC vanilla_allocation_ratio=%s\n' "$vanilla_allocation_ratio"
printf 'METRIC cookie_allocation_ratio=%s\n' "$cookie_allocation_ratio"
printf 'METRIC hyperlife_allocation_ratio=%s\n' "$hyperlife_allocation_ratio"
printf 'METRIC dsides_allocation_ratio=%s\n' "$dsides_allocation_ratio"
printf 'METRIC corpus_allocated_bytes=%s\n' "${current[corpus_allocated_bytes]}"
printf 'METRIC synthetic_allocated_bytes=%s\n' "${current[synthetic_allocated_bytes]}"
printf 'METRIC candidate_compressed_bytes=%s\n' "${current[candidate_compressed_bytes]}"
printf 'METRIC source_compressed_bytes=%s\n' "${current[source_compressed_bytes]}"
printf 'METRIC decompressed_bytes=%s\n' "${current[decompressed_bytes]}"
printf 'METRIC snapshot_count=%s\n' "${current[snapshot_count]}"
printf 'METRIC working_ms=%s\n' "${current[working_ms]}"
printf 'METRIC working_ratio=%s\n' "$working_ratio"
printf 'METRIC cpu_ms=%s\n' "${current[cpu_ms]}"
printf 'METRIC cpu_ratio=%s\n' "$cpu_ratio"
printf 'METRIC peak_working_set_bytes=%s\n' "${current[peak_working_set_bytes]}"
printf 'METRIC peak_working_set_ratio=%s\n' "$peak_working_set_ratio"
