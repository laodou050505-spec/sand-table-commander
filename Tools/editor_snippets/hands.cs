var ids = WarTable.EditorTools.DemoFigures.Gears.Keys.ToArray();
var stage = new GameObject("TMP_Stage"); stage.transform.position = new Vector3(20, 0, 0); stage.transform.localScale = Vector3.one * 10f;
var mode = System.IO.File.ReadAllText("/tmp/sp2/hands_mode.txt").Trim();
for (int i = 0; i < ids.Length; i++) {
  string id = ids[i]; var gear = WarTable.EditorTools.DemoFigures.GearOf(id, "gothic");
  var pf = AssetDatabase.LoadAssetAtPath<GameObject>(WarTable.EditorTools.DemoFigures.PrefabPath(id, "gothic"));
  var g = (GameObject)PrefabUtility.InstantiatePrefab(pf, stage.transform);
  g.transform.localPosition = new Vector3(i * 0.3f, 0, 0); g.transform.localRotation = Quaternion.identity;
  var rider = WarTable.EditorTools.DemoFigures.Find(g.transform, "Rider");
  string clipName = mode == "ready" ? gear.ready : (gear.grip == "GripL" ? "AttackBow" : gear.ready == "ReadyRifle" ? "AttackRifle" : gear.ready == "HoldThrown" ? "AttackThrown" : gear.ready == "Ready2HL" ? "ThrustPole" : gear.ready == "Ready2H" ? "Swing2H_R" : gear.ready == "Mount" ? "Mount" : "Swing1H_R");
  var clip = WarTable.EditorTools.DemoFigures.Clip("troops", "g_" + id, clipName);
  if (clip) clip.SampleAnimation(rider.gameObject, clip.length * (mode == "ready" ? 0.3f : 0.45f));
  if (gear.mount != null) WarTable.EditorTools.DemoFigures.Clip("mounts", gear.mount, "Stand")?.SampleAnimation(WarTable.EditorTools.DemoFigures.Find(g.transform, "Mount").gameObject, 0.2f);
  var grip = WarTable.EditorTools.DemoFigures.Find(rider, gear.grip);
  var c = grip.position;
  var eye = c + g.transform.forward * 0.2f + g.transform.right * 0.11f + Vector3.up * 0.04f;
  WarTable.EditorTools.WTCapture.Shot($"/tmp/sp2/hands/{mode}_{i:00}_{id}.png", eye, c, 40f);
}
Object.DestroyImmediate(stage); result.Log("ok");
