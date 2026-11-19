using System.Globalization;
using Brutal.Numerics;

namespace BrutalFps.Engine;

/// <summary>
/// Loads a Wavefront OBJ (as exported from Blender: triangulated, with normals) into one <see cref="Mesh"/>
/// per object, keyed by object name (the "o" lines), so parts can be coloured and animated separately.
/// </summary>
public static class ObjModel
{
    public static Dictionary<string, Mesh> Load(string path)
    {
        var positions = new List<float3>();
        var normals = new List<float3>();
        var parts = new Dictionary<string, List<float>>();
        var current = new List<float>();
        parts["default"] = current;

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (fields[0])
            {
                case "o":
                case "g":
                    var name = fields.Length > 1 ? fields[1] : "default";
                    if (!parts.TryGetValue(name, out current!))
                    {
                        current = [];
                        parts[name] = current;
                    }
                    break;

                case "v":
                    positions.Add(new float3(F(fields[1]), F(fields[2]), F(fields[3])));
                    break;

                case "vn":
                    normals.Add(new float3(F(fields[1]), F(fields[2]), F(fields[3])));
                    break;

                case "f":
                    // Triangulate as a fan, in case the file has quads or n-gons.
                    for (var i = 2; i + 1 < fields.Length; i++)
                    {
                        AddVertex(fields[1]);
                        AddVertex(fields[i]);
                        AddVertex(fields[i + 1]);
                    }
                    break;
            }
        }

        var result = new Dictionary<string, Mesh>();
        foreach (var (name, data) in parts)
        {
            if (data.Count > 0)
                result[name] = new Mesh([.. data]);
        }

        return result;

        void AddVertex(string token)
        {
            // v, v/vt, v//vn or v/vt/vn; indices are 1-based (negative = relative to the end).
            var refs = token.Split('/');
            var p = positions[Index(refs[0], positions.Count)];
            var n = refs.Length >= 3 && refs[2].Length > 0 ? normals[Index(refs[2], normals.Count)] : float3.UnitY;
            current.Add(p.X);
            current.Add(p.Y);
            current.Add(p.Z);
            current.Add(n.X);
            current.Add(n.Y);
            current.Add(n.Z);
        }
    }

    private static int Index(string token, int count)
    {
        var i = int.Parse(token, CultureInfo.InvariantCulture);
        return i < 0 ? count + i : i - 1;
    }

    private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
}
