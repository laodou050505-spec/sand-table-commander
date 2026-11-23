var ids = System.IO.File.ReadAllText("/tmp/sp2/stage_ids.txt").Trim().Split(' ');
var stage = new GameObject("TMP_Stage"); stage.transform.position = new Vector3(20, 0, 0);
var gnd = GameObject.CreatePrimitive(PrimitiveType.Plane); gnd.transform.SetParent(stage.transform, false); gnd.transform.localScale = Vector3.one * 0.3f;
gnd.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_WarTable/Art/Materials/M_FrameWalnut.mat");
for (int i = 0; i < ids.Length; i++) {
  var parts = ids[i].Split(':'); string id = parts[0]; string era = parts.Length > 1 ? parts[1] : null;
  var pf = AssetDatabase.LoadAssetAtPath<GameObject>(WarTable.EditorTools.DemoFigures.PrefabPath(id, era));
  var g = (GameObject)PrefabUtility.InstantiatePrefab(pf, stage.transform);
  g.transform.localPosition = new Vector3(i * 0.5f - 2f, 0, 0); g.transform.localRotation = Quaternion.LookRotation(Vector3.forward);
  WarTable.EditorTools.DemoFigures.Pose(g, id, 0.3f, era);
  bool mounted = id.Contains("knight") || id.Contains("wolf");
  var c = g.transform.position + Vector3.up * (mounted ? 0.03f : 0.022f);
  float d = mounted ? 0.13f : 0.1f;
  WarTable.EditorTools.WTCapture.Shot($"/tmp/sp2/each/{i:00}_{id}_f.png", c + new Vector3(d * 0.35f, d * 0.25f, d), c, 30f);
  WarTable.EditorTools.WTCapture.Shot($"/tmp/sp2/each/{i:00}_{id}_b.png", c + new Vector3(-d * 0.35f, d * 0.25f, -d), c, 30f);
}
Object.DestroyImmediate(stage); result.Log("ok " + ids.Length);
