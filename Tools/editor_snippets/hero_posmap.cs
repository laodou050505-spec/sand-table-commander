// 封面主角的“位置图”：把 LOD0 的三角形按 UV 光栅化，RGB = 该点在整尊像包围盒里的归一化 x / y / z → /tmp/sp6/hero_pos.png
// 给 Tools/hero_emission.py 用（区分岩座的熔岩和肩甲的警示纹、圈出面罩镜片）。用法：~/.unity/wartable-tools/cs.py "$(cat Tools/editor_snippets/hero_posmap.cs)"
var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
var cov = roots.First(g => g.name == "Cover");
var hero = cov.GetComponentInChildren<WarTable.Game.CoverHero>(true);
var mf = hero.statue.Find("LOD0").GetComponent<MeshFilter>(); var mesh = mf.sharedMesh; var tr = mf.transform;
var vs = mesh.vertices; var uvs = mesh.uv; var tris = mesh.triangles;
var wp = new Vector3[vs.Length]; Vector3 mn = Vector3.one * 1e9f, mx = Vector3.one * -1e9f;
for (int i = 0; i < vs.Length; i++) { wp[i] = cov.transform.InverseTransformPoint(tr.TransformPoint(vs[i])); mn = Vector3.Min(mn, wp[i]); mx = Vector3.Max(mx, wp[i]); }
var sz = mx - mn; int N = 1024; var px = new Color32[N * N];
for (int t = 0; t < tris.Length; t += 3) {
  Vector2 a = uvs[tris[t]] * N, b = uvs[tris[t + 1]] * N, c = uvs[tris[t + 2]] * N;
  float den = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y); if (Mathf.Abs(den) < 1e-9f) continue;
  int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))) - 1), x1 = Mathf.Min(N - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))) + 1);
  int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))) - 1), y1 = Mathf.Min(N - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))) + 1);
  for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) {
    float pxx = x + 0.5f, pyy = y + 0.5f;
    float w0 = ((b.y - c.y) * (pxx - c.x) + (c.x - b.x) * (pyy - c.y)) / den, w1 = ((c.y - a.y) * (pxx - c.x) + (a.x - c.x) * (pyy - c.y)) / den, w2 = 1f - w0 - w1;
    if (w0 < -0.02f || w1 < -0.02f || w2 < -0.02f) continue;
    var p = wp[tris[t]] * w0 + wp[tris[t + 1]] * w1 + wp[tris[t + 2]] * w2;
    px[y * N + x] = new Color32((byte)Mathf.Clamp(255f * (p.x - mn.x) / sz.x, 0, 255), (byte)Mathf.Clamp(255f * (p.y - mn.y) / sz.y, 0, 255), (byte)Mathf.Clamp(255f * (p.z - mn.z) / sz.z, 0, 255), 255);
  } }
var tex = new Texture2D(N, N, TextureFormat.RGBA32, false); tex.SetPixels32(px); tex.Apply();
System.IO.File.WriteAllBytes("/tmp/sp6/hero_pos.png", tex.EncodeToPNG()); Object.DestroyImmediate(tex);
result.Log("范围 min=" + mn.ToString("F3") + " max=" + mx.ToString("F3"));
