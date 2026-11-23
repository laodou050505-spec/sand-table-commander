var ids = System.IO.File.ReadAllText("/tmp/sp2/stage_ids.txt").Trim().Split(' ');
var stage = new GameObject("TMP_Stage"); stage.transform.position = new Vector3(20, 0, 0);
var gnd = GameObject.CreatePrimitive(PrimitiveType.Plane); gnd.transform.SetParent(stage.transform, false); gnd.transform.localScale = Vector3.one * 0.1f;
gnd.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_WarTable/Art/Materials/M_FrameWalnut.mat");
int n = ids.Length; float sp = 0.06f;
for (int i = 0; i < n; i++) {
  var parts = ids[i].Split(':'); string id = parts[0]; string era = parts.Length > 1 ? parts[1] : null;
  var pf = AssetDatabase.LoadAssetAtPath<GameObject>(WarTable.EditorTools.DemoFigures.PrefabPath(id, era));
  if (!pf) { result.Log("缺预制体 " + ids[i]); continue; }
  var g = (GameObject)PrefabUtility.InstantiatePrefab(pf, stage.transform);
  g.transform.localPosition = new Vector3((i - (n - 1) * 0.5f) * sp, 0, 0);
  g.transform.localRotation = Quaternion.LookRotation(Vector3.forward);
  WarTable.EditorTools.DemoFigures.Pose(g, id, 0.3f, era);
}
var c = stage.transform.position + Vector3.up * 0.025f; float w = Mathf.Max(0.12f, n * sp * 0.9f);
result.Log(WarTable.EditorTools.WTCapture.Shot("/tmp/sp2/stage_front.png", c + new Vector3(0, 0.025f, w * 1.15f), c, 40f));
result.Log(WarTable.EditorTools.WTCapture.Shot("/tmp/sp2/stage_back.png", c + new Vector3(w * 0.5f, 0.03f, -w * 1.3f), c, 40f));
Object.DestroyImmediate(stage);
