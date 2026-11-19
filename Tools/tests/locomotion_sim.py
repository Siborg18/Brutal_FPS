"""Port of Animator.LocomotionWeights / PhaseRate (Engine/Animator.cs) for a foot-slide check.

Speed ramp 0 -> 3.68 -> 1.16 -> 0 m/s over 6 s at 60 Hz, with the CONTRACTS clip strides and durations.
Error proxy: |groundSpeed - blendedStride * phaseRate|, where blendedStride is the pose-weighted stride
(idle contributes 0) and phaseRate is the cycles/s the shared Walk/Run phase advances at.
Also counts the Walk/Run foot events that fire (two per cycle).
Run: python3 Tools/tests/locomotion_sim.py
"""
import math

WALK_STRIDE, WALK_DUR = 1.24, 32 / 30
RUN_STRIDE, RUN_DUR = 2.70, 22 / 30
WALK_SPEED, RUN_SPEED = WALK_STRIDE / WALK_DUR, RUN_STRIDE / RUN_DUR


def weights(speed, has_run=True):
    w_idle = w_walk = w_run = 0.0
    if not has_run or RUN_SPEED <= WALK_SPEED + 1e-3:
        w_walk = min(speed / WALK_SPEED, 1.0)
        w_idle = 1 - w_walk
        return w_idle, w_walk, 0.0, w_walk * WALK_STRIDE
    if speed < WALK_SPEED:                      # idle <-> walk: linear in speed
        w_walk = speed / WALK_SPEED
        w_idle = 1 - w_walk
    else:                                       # walk <-> run: smoothstep
        x = min(max((speed - WALK_SPEED) / (RUN_SPEED - WALK_SPEED), 0.0), 1.0)
        t = x * x * (3 - 2 * x)
        w_run, w_walk = t, 1 - t
    return w_idle, w_walk, w_run, w_walk * WALK_STRIDE + w_run * RUN_STRIDE


def phase_rate(speed, stride, has_run=True):
    if stride <= 1e-4:
        return 0.0
    rate = speed / stride
    if not has_run:
        rate = min(rate, 1.6 / WALK_DUR)
    return rate


def speed_at(t):
    if t < 2:
        return 3.68 * t / 2
    if t < 4:
        return 3.68 + (1.16 - 3.68) * (t - 2) / 2
    return 1.16 * (1 - (t - 4) / 2)


def run(has_run=True):
    dt, phase, worst, worst_at, rates = 1 / 60, 0.0, 0.0, 0.0, []
    events, cycles = 0, 0.0
    for k in range(6 * 60):
        s = speed_at(k * dt)
        wi, ww, wr, stride = weights(s, has_run)
        rate = phase_rate(s, stride, has_run)
        p0, p1 = phase, phase + rate * dt
        phase = p1 - math.floor(p1)
        cycles += rate * dt
        dur = RUN_DUR if wr > 0.5 else WALK_DUR
        evs = (0.0, 0.3667 / dur) if wr > 0.5 else (0.0, 0.5333 / dur)
        if ww > 0 or wr > 0:
            for e in evs:
                if math.floor(p1 - e) > math.floor(p0 - e):
                    events += 1
        if s > 0.05:
            err = abs(s - stride * rate)
            if err > worst:
                worst, worst_at = err, s
            rates.append(rate)
        assert abs(wi + ww + wr - 1) < 1e-9
    return worst, worst_at, min(rates), max(rates), events, cycles


if __name__ == "__main__":
    for has_run in (True, False):
        worst, at, lo, hi, ev, cyc = run(has_run)
        label = "with Run" if has_run else "no Run (walk capped 1.6x)"
        print(f"{label}: max |speed - stride*rate| = {worst:.2e} m/s (at {at:.2f} m/s); "
              f"phase rate {lo:.3f}..{hi:.3f} cycles/s; {cyc:.2f} cycles, {ev} foot events")
        if has_run:
            assert worst < 1e-3, "foot slide proxy too large"
    print("PASS")
