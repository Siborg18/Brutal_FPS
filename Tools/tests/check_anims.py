"""Acceptance checks for Assets/Models/anims/human_anims.glb (task S4). Reads the glb with numpy only:

    python3 Tools/tests/check_anims.py            # prints every measured number, exits 1 if a check fails

Coordinates: glTF is Y-up, Blender is Z-up with the figure facing -Y; the Blender position is (x, -z_gltf, y_gltf).
"""
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from glbread import Glb, Rig, quat_angle_deg, sample  # noqa: E402

PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
ANIMS = os.path.join(PROJECT, "Assets", "Models", "anims", "human_anims.glb")
HUMAN = os.path.join(PROJECT, "Assets", "Models", "human.glb")
PREVIEWS = os.path.join(PROJECT, "Tools", "previews", "models")
FPS = 30.0

EXPECT = {  # name: frames (CONTRACTS section 3)
    "Run": 22, "IdleLook": 150, "IdleShift": 120, "ShuffleTurn": 24,
    "Cower": 60, "DeathFwd": 54, "DeathBack": 54, "Talk": 120,
}
LOOPERS = ("Run", "Cower", "Talk")
IDLE_ONESHOTS = ("IdleLook", "IdleShift", "ShuffleTurn")
RUN_STRIDE, RUN_STANCE_FRAMES = 2.70, 5
RUN_SPEED = RUN_STRIDE / (EXPECT["Run"] / FPS)

results = []


def report(name, ok, detail):
    results.append(ok)
    print(f"{'PASS' if ok else 'FAIL'}  {name}: {detail}")


def to_blender(p):
    return np.array([p[0], -p[2], p[1]])


def main():
    ga, gh = Glb(ANIMS), Glb(HUMAN)
    anims = ga.animations()
    human_nodes = {n["name"] for n in gh.nodes}
    rig = Rig(ga)

    # 1 ---- exactly the 8 animations, every channel targets a node that exists in human.glb
    names = sorted(anims)
    bad = sorted({nm for a in anims.values() for nm in a if nm not in human_nodes})
    ok = names == sorted(EXPECT) and not bad
    nch = {k: sum(len(v) for v in a.values()) for k, a in anims.items()}
    report("1 names", ok, f"{len(names)} animations {names}; channels {nch}; nodes missing from human.glb: {bad or 'none'}")

    # 2 ---- duration = frames / 30 +- 1 frame
    worst = 0.0
    parts = []
    dur = {}
    for n, fr in EXPECT.items():
        d = ga.duration(anims[n])
        dur[n] = d
        err = abs(d - fr / FPS) * FPS
        worst = max(worst, err)
        parts.append(f"{n} {d:.4f}s ({d * FPS:.2f} f of {fr})")
    report("2 duration", worst <= 1.0, f"worst error {worst:.3f} frames; " + ", ".join(parts))

    def pose_at(name, t):
        """{node name: (translation, rotation)} for every node with a channel."""
        out = {}
        for nd, ch in anims[name].items():
            tr = sample(*ch["translation"][:2], t, "translation") if "translation" in ch else None
            q = sample(*ch["rotation"][:2], t, "rotation") if "rotation" in ch else None
            out[nd] = (tr, q)
        return out

    # 3 ---- loopers: first frame = last frame
    parts, worst = [], 0.0
    for n in LOOPERS:
        p0, p1 = pose_at(n, 0.0), pose_at(n, dur[n])
        m = max((quat_angle_deg(p0[b][1], p1[b][1]) for b in p0 if p0[b][1] is not None), default=0.0)
        worst = max(worst, m)
        parts.append(f"{n} {m:.4f} deg")
    report("3 loops close", worst <= 0.5, f"max bone rotation difference first vs last: " + ", ".join(parts))

    # 4 ---- Idle-variant one-shots start and end on Idle frame 1
    idle = gh.animations()["Idle"]
    idle0 = {nd: sample(*ch["rotation"][:2], 0.0, "rotation") for nd, ch in idle.items() if "rotation" in ch}
    parts, worst = [], 0.0
    for n in IDLE_ONESHOTS:
        res = []
        for t in (0.0, dur[n]):
            p = pose_at(n, t)
            m, who = 0.0, ""
            for b, q0 in idle0.items():
                q = p[b][1] if b in p and p[b][1] is not None else np.array(gh.nodes[gh.by_name[b]].get("rotation", [0, 0, 0, 1]), dtype=float)
                a = quat_angle_deg(q, q0)
                if a > m:
                    m, who = a, b
            res.append((m, who))
        worst = max(worst, max(r[0] for r in res))
        parts.append(f"{n} first {res[0][0]:.3f} ({res[0][1]}) last {res[1][0]:.3f} ({res[1][1]})")
    report("4 start/end on Idle frame 1", worst <= 2.0, f"worst bone difference {worst:.3f} deg; " + "; ".join(parts))

    # 5 ---- Run foot sliding
    N = EXPECT["Run"]
    anim = anims["Run"]
    ball = {s: gh.by_name["ball" + s] for s in (".L", ".R")}
    ballA = {s: ga.by_name["ball" + s] for s in (".L", ".R")}
    pos = {s: [] for s in ball}
    for k in range(N + 1):
        W = rig.world(anim, k / FPS)
        for s in ball:
            pos[s].append(to_blender(W[ballA[s]][:3, 3]))
    pos = {s: np.array(v) for s, v in pos.items()}
    detail, ok5, speeds = [], True, []
    for s, f0 in ((".L", 0), (".R", N // 2)):
        idx = list(range(f0, f0 + RUN_STANCE_FRAMES + 1))          # stance: heel strike at phase 0 (L) / 0.5 (R)
        seg = pos[s][idx]
        vy = np.diff(seg[:, 1]) * FPS                  # +Y is backwards (the figure faces -Y)
        mean = float(vy.mean())
        dev = float(np.max(np.abs(vy - RUN_SPEED)) / RUN_SPEED * 100)
        spread = float(seg[:, 2].max() - seg[:, 2].min())
        speeds += list(vy)
        ok5 &= dev <= 5.0 and spread <= 0.01
        detail.append(f"{s[1]}: stance frames {idx[0]}..{idx[-1]} mean {mean:.3f} m/s (target {RUN_SPEED:.3f}), worst frame dev "
                      f"{dev:.2f}%, ball height spread {spread * 1000:.1f} mm")
    report("5 run foot sliding", ok5, "; ".join(detail) + f"; mean over both stances {np.mean(speeds):.3f} m/s")

    # 5b ---- Run reads as a jog, not a crouch: pelvis bob and trailing knee height
    rest_pelvis = float(Rig(gh).world(None, 0.0)[gh.by_name["pelvis"]][1, 3])
    pel, knee_tr = [], []
    for k in range(N + 1):
        W = rig.world(anim, k / FPS)
        pel.append(float(W[ga.by_name["pelvis"]][1, 3]))
        trailing = ".L" if pos[".L"][k][1] > pos[".R"][k][1] else ".R"          # the foot further back (+Y)
        knee_tr.append(float(W[ga.by_name["calf" + trailing]][1, 3]))
    report("9 run pelvis height", min(pel) >= rest_pelvis - 0.06,
           f"rest pelvis z {rest_pelvis:.3f}; min over the cycle {min(pel):.3f} ({(min(pel) - rest_pelvis) * 100:+.1f} cm), "
           f"max {max(pel):.3f} ({(max(pel) - rest_pelvis) * 100:+.1f} cm)")
    report("10 run trailing knee", min(knee_tr) >= 0.30, f"lowest trailing-knee height over all frames {min(knee_tr):.3f} m")

    # 6 ---- ground contact with the deformed Human mesh; Death pelvis height
    node = next(n for n in ga.nodes if "skin" in n and "mesh" in n)
    skin = ga.json["skins"][node["skin"]]
    joints = skin["joints"]
    ibm = ga.accessor(skin["inverseBindMatrices"]).reshape(-1, 4, 4).transpose(0, 2, 1)       # column-major to row-major
    verts, jidx, wts = [], [], []
    for prim in ga.json["meshes"][node["mesh"]]["primitives"]:
        verts.append(ga.accessor(prim["attributes"]["POSITION"]).astype(np.float64))
        jidx.append(ga.accessor(prim["attributes"]["JOINTS_0"]).astype(np.int64))
        wts.append(ga.accessor(prim["attributes"]["WEIGHTS_0"]).astype(np.float64))
    V = np.concatenate(verts)
    J = np.concatenate(jidx)
    Wt = np.concatenate(wts)
    Vh = np.concatenate([V, np.ones((len(V), 1))], axis=1)
    low, lowat = {}, {}
    pelvis_last = {}
    pelvis_node = ga.by_name["pelvis"]
    for n, fr in EXPECT.items():
        m = 1e9
        for k in range(fr + 1):
            Wd = rig.world(anims[n], k / FPS)
            S = np.stack([Wd[j] @ ibm[i] for i, j in enumerate(joints)])           # (joints, 4, 4)
            P = np.zeros((len(V), 3))
            for c in range(4):
                P += Wt[:, c:c + 1] * np.einsum("nij,nj->ni", S[J[:, c]], Vh)[:, :3]
            z = float(P[:, 1].min())                                            # glTF Y is up
            if z < m:
                m, lowat[n] = z, k
            if k == fr:
                pelvis_last[n] = float(Wd[pelvis_node][1, 3])
        low[n] = m
    worst = min(low.values())
    d6 = ", ".join(f"{n} {low[n] * 1000:.1f} mm (frame {lowat[n]})" for n in EXPECT)
    pel = ", ".join(f"{n} {pelvis_last[n]:.3f} m" for n in ("DeathFwd", "DeathBack"))
    report("6 ground", worst >= -0.01 and all(pelvis_last[n] < 0.25 for n in ("DeathFwd", "DeathBack")),
           f"lowest vertex z per clip: {d6}; pelvis z at last frame: {pel}")

    # 7 ---- ShuffleTurn: no net pelvis rotation
    p0, p1 = pose_at("ShuffleTurn", 0.0), pose_at("ShuffleTurn", dur["ShuffleTurn"])
    a = quat_angle_deg(p0["pelvis"][1], p1["pelvis"][1])
    report("7 shuffle net rotation", a <= 2.0, f"pelvis first vs last {a:.3f} deg")

    # 8 ---- previews exist (their content is judged by eye)
    missing = [n for n in EXPECT if not os.path.exists(os.path.join(PREVIEWS, f"anim_{n}.png"))]
    report("8 previews", not missing, f"missing: {missing or 'none'}")

    print("ALL PASS" if all(results) else "SOME CHECKS FAILED")
    return 0 if all(results) else 1


if __name__ == "__main__":
    sys.exit(main())
