"""Extracts the GLSL raw strings (VertexSource / FragmentSource and any other `const string XxxSource = \"\"\"`)
from Engine/Renderer.cs and validates each with glslangValidator. Usage: python3 Tools/check_shaders.py"""
import re, subprocess, sys, os, tempfile
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GV = os.environ.get("GLSLANG", "/opt/tools/glslangValidator")
src = open(os.path.join(ROOT, "Engine", "Renderer.cs"), encoding="utf-8").read()
ok = True
for m in re.finditer(r'const string (\w+Source)\s*=\s*"""\n(.*?)\n\s*""";', src, re.S):
    name, body = m.group(1), m.group(2)
    lines = body.split("\n"); ind = min(len(l) - len(l.lstrip()) for l in lines if l.strip())
    glsl = "\n".join(l[ind:] for l in lines)
    stage = "vert" if "Vertex" in name else "frag" if "Fragment" in name else None
    if stage is None:
        stage = "frag" if "gl_FragColor" in glsl or "out vec4" in glsl and "gl_Position" not in glsl else "vert"
    f = os.path.join(tempfile.gettempdir(), f"{name}.{stage}")
    open(f, "w").write(glsl)
    r = subprocess.run([GV, f], capture_output=True, text=True)
    good = r.returncode == 0
    ok &= good
    print(f"{name} ({stage}): {'PASS' if good else 'FAIL'}")
    if not good:
        print(r.stdout[-3000:])
print("ALL PASS" if ok else "FAILURES")
sys.exit(0 if ok else 1)
