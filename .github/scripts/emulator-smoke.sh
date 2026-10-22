#!/usr/bin/env bash
# Smoke test on the Android emulator, run by reactivecircus/android-emulator-runner:
#   script: bash .github/scripts/emulator-smoke.sh <apk_dir> <out_dir>
# (the action runs each `script:` line in its own shell, so all logic lives here).
#
# Installs the SB_SELFTEST APK, measures the cold start (am start -W TotalTime),
# waits for the "SB_SELFTEST|DONE|n_ok/n_total" logcat line, records dumpsys meminfo,
# writes everything to the job summary and fails if any self-test step is not OK.
# Emulator numbers are NOT phone numbers: they are published as emulator measurements.
set -euo pipefail
trap 'echo "::error title=emulator-smoke.sh::line ${LINENO}: ${BASH_COMMAND}"' ERR

PACKAGE="${APP_PACKAGE_ID:-com.smartbudgetml.app}"
APK_DIR="${1:-./apk-selftest}"
OUT_DIR="${2:-./emulator-results}"
TIMEOUT_S="${SELFTEST_TIMEOUT_S:-120}"
SUMMARY="${GITHUB_STEP_SUMMARY:-/dev/null}"
mkdir -p "$OUT_DIR"

APK_FILE="$(find "$APK_DIR" -name '*-Signed.apk' | head -n 1)"
if [ -z "$APK_FILE" ]; then
  echo "::error title=emulator-smoke.sh::no *-Signed.apk found in $APK_DIR"
  exit 1
fi
echo "Installing $APK_FILE"
adb install -r "$APK_FILE"

# Resolve the launcher activity instead of guessing the crc64 class name.
ACTIVITY="$(adb shell cmd package resolve-activity --brief "$PACKAGE" | tr -d '\r' | tail -n 1)"
case "$ACTIVITY" in
  */*) echo "Launch activity: $ACTIVITY" ;;
  *) echo "::error title=emulator-smoke.sh::cannot resolve launch activity for $PACKAGE (got: $ACTIVITY)"; exit 1 ;;
esac

adb logcat -c
START_OUT="$(adb shell am start -W -n "$ACTIVITY" | tr -d '\r')"
echo "$START_OUT"
TOTAL_TIME="$(printf '%s\n' "$START_OUT" | awk -F': ' '/^TotalTime/ {print $2}')"
if [ -z "$TOTAL_TIME" ]; then
  TOTAL_TIME="n/a"
fi

SELFTEST_LOG="$OUT_DIR/selftest.txt"
DONE_LINE=""
elapsed=0
while [ "$elapsed" -lt "$TIMEOUT_S" ]; do
  adb logcat -d -v raw -s SB_SELFTEST:I > "$SELFTEST_LOG"
  if DONE_LINE="$(grep -m 1 '^SB_SELFTEST|DONE|' "$SELFTEST_LOG")"; then
    break
  fi
  sleep 2
  elapsed=$((elapsed + 2))
done

MEMINFO="$(adb shell dumpsys meminfo "$PACKAGE" | tr -d '\r')"
printf '%s\n' "$MEMINFO" > "$OUT_DIR/meminfo.txt"
TOTAL_PSS="$(printf '%s\n' "$MEMINFO" | awk '/TOTAL PSS:/ {print $3; exit} /^ *TOTAL:/ {print $2; exit}')"
if [ -z "$TOTAL_PSS" ]; then
  TOTAL_PSS="n/a"
fi
adb logcat -d > "$OUT_DIR/logcat-full.txt"

STATUS="OK"
REASON=""
if [ -z "$DONE_LINE" ]; then
  STATUS="ECHEC"
  REASON="pas de ligne SB_SELFTEST|DONE apres ${TIMEOUT_S} s"
else
  COUNTS="${DONE_LINE##*|}"
  N_OK="${COUNTS%%/*}"
  N_TOTAL="${COUNTS##*/}"
  if [ "$N_OK" != "$N_TOTAL" ] || [ "$N_TOTAL" = "0" ]; then
    STATUS="ECHEC"
    REASON="$COUNTS etapes OK"
  fi
fi
if grep -q -E '^SB_SELFTEST\|[^|]+\|(FAIL|FATAL)' "$SELFTEST_LOG"; then
  STATUS="ECHEC"
  REASON="${REASON:+$REASON; }au moins une etape FAIL/FATAL"
fi

{
  echo "## Auto-test sur emulateur (mesures d'emulateur, pas d'un telephone)"
  echo "- Activite : \`$ACTIVITY\`"
  echo "- Demarrage a froid (am start -W, TotalTime) : **${TOTAL_TIME} ms**"
  echo "- Memoire apres l'auto-test (dumpsys meminfo, TOTAL PSS) : **${TOTAL_PSS} Ko**"
  echo "- Resultat : **${STATUS}**${REASON:+ ($REASON)}"
  echo ""
  echo "| Etape | Statut | Duree (ms) |"
  echo "|---|---|---|"
  while IFS='|' read -r tag step state ms rest; do
    if [ "$tag" = "SB_SELFTEST" ] && [ "$step" != "DONE" ]; then
      echo "| $step | $state | $ms |"
    fi
  done < "$SELFTEST_LOG"
  echo ""
  echo "\`${DONE_LINE:-SB_SELFTEST|DONE manquant}\`"
} >> "$SUMMARY"

echo "--- SB_SELFTEST lines ---"
cat "$SELFTEST_LOG"
echo "Result: $STATUS ${REASON}"

# Annotations are readable without signing in (job logs are not): surface the
# result, every failed step and the app's fatal errors there.
echo "::notice title=Emulator self-test::${DONE_LINE:-no DONE line}, TotalTime=${TOTAL_TIME} ms, PSS=${TOTAL_PSS} KB"
if [ "$STATUS" != "OK" ]; then
  echo "::error title=Emulator self-test::${REASON}"
  if FAILED_STEPS="$(grep -E '^SB_SELFTEST\|[^|]+\|(FAIL|FATAL)' "$SELFTEST_LOG")"; then
    printf '%s\n' "$FAILED_STEPS" | head -n 10 | while IFS= read -r line; do
      echo "::error title=SB_SELFTEST step::${line}"
    done
  fi
  if CRASH_LINES="$(grep -E 'FATAL EXCEPTION|AndroidRuntime: |Unhandled|monodroid.*(error|Error)' "$OUT_DIR/logcat-full.txt")"; then
    printf '%s\n' "$CRASH_LINES" | head -n 8 | while IFS= read -r line; do
      echo "::error title=logcat::${line:0:400}"
    done
  fi
  exit 1
fi
