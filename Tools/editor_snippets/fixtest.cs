string[] ids = { "kd_footman", "kd_hammer", "kd_halberdier", "cl_berserker" };
var stage = new GameObject("TMP_Stage"); stage.transform.position = new Vector3(20, 0, 0); stage.transform.localScale = Vector3.one * 10f;
for (int pass = 0; pass < 2; pass++)
for (int i = 0; i < ids.Length; i++)
{
  string id = ids[i]; var gear = WarTable.EditorTools.DemoFigures.GearOf(id, null);
  var pf = AssetDatabase.LoadAssetAtPath<GameObject>(WarTable.EditorTools.DemoFigures.PrefabPath(id, null));
  var g = (GameObject)PrefabUtility.InstantiatePrefab(pf, stage.transform);
  g.transform.localPosition = new Vector3(i * 0.3f, 0, pass * 0.5f); g.transform.localRotation = Quaternion.identity;
  var rider = WarTable.EditorTools.DemoFigures.Find(g.transform, "Rider");
  var grip = WarTable.EditorTools.DemoFigures.Find(rider, gear.grip);
  if (pass == 1)
  {
    var R = Quaternion.Euler(gear.grip == "GripL" ? WarTable.EditorTools.DemoFigures.GripLFix : WarTable.EditorTools.DemoFigures.GripRFix);
    foreach (Transform c in grip) c.localPosition = R * c.localPosition;
  }
  WarTable.EditorTools.DemoFigures.Pose(g, id, 0.3f, null);
  var c0 = grip.position;
  WarTable.EditorTools.WTCapture.Shot($"/tmp/sp2/fix/{pass}_{i}_{id}.png", c0 + g.transform.forward * 0.2f + g.transform.right * 0.11f + Vector3.up * 0.04f, c0, 40f, 800, 800);
}
Object.DestroyImmediate(stage); result.Log("ok");
