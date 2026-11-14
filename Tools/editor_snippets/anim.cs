var stage = new GameObject("TMP_Stage"); stage.transform.position = new Vector3(20, 0, 0);
var gnd = GameObject.CreatePrimitive(PrimitiveType.Plane); gnd.transform.SetParent(stage.transform, false); gnd.transform.localScale = Vector3.one * 0.1f;
gnd.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_WarTable/Art/Materials/M_FrameWalnut.mat");
var names = AssetDatabase.LoadAllAssetsAtPath("Assets/_WarTable/Art/Models/Demo/troops/g_kd_footman.fbx").OfType<AnimationClip>().Where(c => !c.name.StartsWith("__")).Select(c => c.name.Substring(c.name.LastIndexOf('|') + 1)).ToList();
result.Log(string.Join(",", names));
string[] show = { "Ready1H", "Run", "Swing1H_R", "Death", "Walk", "Block1H" };
var pf = AssetDatabase.LoadAssetAtPath<GameObject>(WarTable.EditorTools.DemoFigures.PrefabPath("kd_footman", "gothic"));
for (int i = 0; i < show.Length; i++) {
  var g = (GameObject)PrefabUtility.InstantiatePrefab(pf, stage.transform);
  g.transform.localPosition = new Vector3((i - 2.5f) * 0.05f, 0, 0);
  g.transform.localRotation = Quaternion.Euler(0, 200, 0);
  var rider = WarTable.EditorTools.DemoFigures.Find(g.transform, "Rider");
  var clip = WarTable.EditorTools.DemoFigures.Clip("troops", "g_kd_footman", show[i]);
  if (clip) clip.SampleAnimation(rider.gameObject, clip.length * 0.45f); else result.Log("无 " + show[i]);
}
var c = stage.transform.position + Vector3.up * 0.022f;
result.Log(WarTable.EditorTools.WTCapture.Shot("/tmp/sp2/anim_row.png", c + new Vector3(0, 0.035f, 0.30f), c, 32f));
result.Log(WarTable.EditorTools.WTCapture.Shot("/tmp/sp2/anim_close.png", c + new Vector3(-0.125f, 0.01f, 0.075f), c + new Vector3(-0.125f, 0, 0), 32f));
Object.DestroyImmediate(stage);
