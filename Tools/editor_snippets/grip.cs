var sb = new System.Text.StringBuilder();
var stage = new GameObject("TMP_Stage"); stage.transform.position = new Vector3(20, 0, 0);
float H = 0.0195f;
foreach (var era in new[] { (string)null, "gothic", "future" })
foreach (var id in WarTable.EditorTools.DemoFigures.Gears.Keys)
{
  var gear = WarTable.EditorTools.DemoFigures.GearOf(id, era);
  var pf = AssetDatabase.LoadAssetAtPath<GameObject>(WarTable.EditorTools.DemoFigures.PrefabPath(id, era));
  if (!pf) { sb.AppendLine(id + " 缺预制体"); continue; }
  var g = (GameObject)PrefabUtility.InstantiatePrefab(pf, stage.transform);
  var rider = WarTable.EditorTools.DemoFigures.Find(g.transform, "Rider");
  string model = WarTable.EditorTools.DemoFigures.ModelOf(id, era);
  string atk = gear.grip == "GripL" ? "AttackBow" : gear.ready == "ReadyRifle" ? "AttackRifle" : gear.ready == "HoldThrown" ? "HoldThrown" : gear.ready == "Ready2HL" ? "ThrustPole" : gear.ready == "Ready2H" ? "Swing2H_R" : gear.ready == "Mount" ? "Couch" : "Swing1H_R";
  string line = (era ?? "medieval") + " " + id + " [" + gear.weapon + "]";
  foreach (var cn in new[] { gear.ready, atk })
  {
    var clip = WarTable.EditorTools.DemoFigures.Clip("troops", model, cn);
    if (!clip) { line += " " + cn + "=无"; continue; }
    clip.SampleAnimation(rider.gameObject, clip.length * 0.4f);
    var grip = WarTable.EditorTools.DemoFigures.Find(rider, gear.grip);
    var w = grip ? grip.Find(gear.weapon) : null;
    if (!w) { line += " 无武器"; continue; }
    string handName = gear.grip == "GripL" ? "LeftHand" : "RightHand";
    var smr = rider.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(x => x.gameObject.activeInHierarchy || true).OrderByDescending(x => x.sharedMesh.vertexCount).First();
    int bi = System.Array.FindIndex(smr.bones, b => b && b.name.EndsWith(handName));
    var baked = new UnityEngine.Mesh(); smr.BakeMesh(baked, true);
    var vs = baked.vertices; var bw = smr.sharedMesh.boneWeights;
    Vector3 sum = Vector3.zero; float ws = 0;
    for (int k = 0; k < vs.Length; k++) { var b = bw[k]; float ww = (b.boneIndex0 == bi ? b.weight0 : 0) + (b.boneIndex1 == bi ? b.weight1 : 0) + (b.boneIndex2 == bi ? b.weight2 : 0) + (b.boneIndex3 == bi ? b.weight3 : 0); if (ww > 0.6f) { sum += smr.transform.TransformPoint(vs[k]) * ww; ws += ww; } }
    Object.DestroyImmediate(baked);
    if (ws <= 0) { line += " 手无顶点"; continue; }
    var palm = sum / ws; float best = 9f;
    foreach (var mf in w.GetComponentsInChildren<MeshFilter>())
    {
      var wv = mf.sharedMesh.vertices.Select(v => mf.transform.TransformPoint(v)).ToArray(); var tr = mf.sharedMesh.triangles;
      for (int t = 0; t < tr.Length; t += 3)
      {
        // 点到三角形最近距离（Ericson）
        Vector3 A = wv[tr[t]], B = wv[tr[t + 1]], C = wv[tr[t + 2]], P = palm, ab = B - A, ac = C - A, ap = P - A; Vector3 cp;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) cp = A;
        else { var bp = P - B; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
          if (d3 >= 0 && d4 <= d3) cp = B;
          else { float vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) cp = A + ab * (d1 / (d1 - d3));
            else { var cpp = P - C; float d5 = Vector3.Dot(ab, cpp), d6 = Vector3.Dot(ac, cpp);
              if (d6 >= 0 && d5 <= d6) cp = C;
              else { float vb = d5 * d2 - d1 * d6;
                if (vb <= 0 && d2 >= 0 && d6 <= 0) cp = A + ac * (d2 / (d2 - d6));
                else { float va = d3 * d6 - d5 * d4;
                  if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0) cp = B + (C - B) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
                  else { float den = 1f / (va + vb + vc); cp = A + ab * (vb * den) + ac * (vc * den); } } } } } }
        float d = Vector3.Distance(cp, P); if (d < best) best = d;
      }
    }
    float toGrip = Vector3.Distance(grip.position, palm), toBone = Vector3.Distance(smr.bones[bi].position, palm);
    line += $" {cn}: 武器离掌心 {best / H * 100:0.0}% 握点离掌心 {toGrip / H * 100:0.0}% (腕到掌心 {toBone / H * 100:0.0}%)";
  }
  sb.AppendLine(line);
  Object.DestroyImmediate(g);
}
Object.DestroyImmediate(stage);
System.IO.File.WriteAllText("/tmp/sp2/grip.txt", sb.ToString()); result.Log("ok");
