var stage = new GameObject("TMP_Stage"); stage.transform.position = new Vector3(20, 0, 0); stage.transform.localScale = Vector3.one * 10f;
string[] ids = { "cl_hunter", "cl_wolfarcher" };
for (int i = 0; i < 2; i++) {
  var pf = AssetDatabase.LoadAssetAtPath<GameObject>(WarTable.EditorTools.DemoFigures.PrefabPath(ids[i], "gothic"));
  var g = (GameObject)PrefabUtility.InstantiatePrefab(pf, stage.transform); g.transform.localPosition = new Vector3(i * 0.5f, 0, 0);
  WarTable.EditorTools.DemoFigures.Pose(g, ids[i], 0.3f, "gothic");
  var c = g.transform.position + Vector3.up * (i == 0 ? 0.11f : 0.2f);
  WarTable.EditorTools.WTCapture.Shot($"/tmp/sp2/two_{i}a.png", c + new Vector3(-0.25f, 0.08f, 0.35f), c, 40f, 700, 700);
  WarTable.EditorTools.WTCapture.Shot($"/tmp/sp2/two_{i}b.png", c + new Vector3(0.35f, 0.08f, 0.2f), c, 40f, 700, 700);
  if (i == 1) foreach (var r in g.GetComponentsInChildren<Renderer>()) if (r.name.Contains("tripo") || r is SkinnedMeshRenderer) result.Log(r.name + " mat=" + r.sharedMaterial.name + " tex=" + r.sharedMaterial.GetTexture("_BaseMap"));
}
Object.DestroyImmediate(stage); result.Log("ok");
