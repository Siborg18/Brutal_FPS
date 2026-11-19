#!/usr/bin/env bash
# Self-test for Tools/validate_skinned.py.
#   1. Runs the validator on the real repo and keeps the output.
#   2. Builds a temp root with a copy of human.glb, a deliberately wrong garment "fake" (sidecar slot "hat"), and a
#      clips.json that lacks Walk. Asserts the expected FAIL lines and that "garment fake bind" PASSes.
# Exit 0 when every assertion holds, 1 otherwise.
set -u

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
VALIDATOR="$ROOT/Tools/validate_skinned.py"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

failures=0
assert_line() {  # assert_line <output-file> <exact line prefix>
    if grep -q "^$2" "$1"; then
        echo "  ok    $2"
    else
        echo "  MISS  $2"
        failures=$((failures + 1))
    fi
}

echo "== 1. real repo"
REAL_OUT="$WORK/real.txt"
python3 "$VALIDATOR" --root "$ROOT" > "$REAL_OUT"
real_rc=$?
cat "$REAL_OUT"
echo "exit code: $real_rc"

echo "== 2. synthetic root"
TMP_ROOT="$WORK/root"
mkdir -p "$TMP_ROOT/Assets/Models/garments" "$TMP_ROOT/Assets/Models/anims"
cp "$ROOT/Assets/Models/human.glb" "$TMP_ROOT/Assets/Models/human.glb"
cp "$ROOT/Assets/Models/human.glb" "$TMP_ROOT/Assets/Models/garments/fake.glb"
cat > "$TMP_ROOT/Assets/Models/garments/fake.json" <<'EOF'
{
  "id": "fake",
  "slot": "hat",
  "layer": 1,
  "offset_mm": 0,
  "hides": ["scalp"],
  "wound": "fabric",
  "materials": ["cloth_tint"],
  "triangles": 1
}
EOF
# clips.json without Walk: Idle only, with the contract values for Idle.
cat > "$TMP_ROOT/Assets/Models/anims/clips.json" <<'EOF'
{
  "fps": 30,
  "clips": {
    "Idle": { "loop": true, "stride": 0, "tags": ["idle"], "events": [] }
  }
}
EOF

SYN_OUT="$WORK/synthetic.txt"
python3 "$VALIDATOR" --root "$TMP_ROOT" > "$SYN_OUT"
syn_rc=$?
cat "$SYN_OUT"
echo "exit code: $syn_rc"

echo "== 3. assertions"
# The real human.glb has no _REGION yet, so "human regions" must FAIL in the synthetic root as well (copy of the same
# file). If a later build adds _REGION, this stays consistent with the real run.
if grep -q "^FAIL human regions" "$REAL_OUT"; then
    assert_line "$SYN_OUT" "FAIL human regions"
else
    assert_line "$SYN_OUT" "PASS human regions"
fi
assert_line "$SYN_OUT" "FAIL garment fake sidecar"
assert_line "$SYN_OUT" "FAIL garment fake catalogue"
assert_line "$SYN_OUT" "FAIL clips coverage"
assert_line "$SYN_OUT" "PASS garment fake bind"
# fake.glb is a copy of the body with offset 0, so the weight match must PASS. It measures about 0.07% rather than 0
# because duplicate-position seam vertices can pick a twin with different weights. Its _REGION is missing.
assert_line "$SYN_OUT" "FAIL garment fake regions"
assert_line "$SYN_OUT" "PASS garment fake weights match"

if [ "$syn_rc" -ne 1 ]; then
    echo "  MISS  synthetic exit code 1 (got $syn_rc)"
    failures=$((failures + 1))
else
    echo "  ok    synthetic exit code 1"
fi
if [ "$real_rc" -ne 1 ] && [ "$real_rc" -ne 0 ]; then
    echo "  MISS  real exit code is 0 or 1 (got $real_rc)"
    failures=$((failures + 1))
fi

if [ "$failures" -eq 0 ]; then
    echo "SELF-TEST PASS"
    exit 0
fi
echo "SELF-TEST FAIL ($failures)"
exit 1
