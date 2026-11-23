// 从玩家眼睛的位置拍封面（封面在眼前 0.8 m、低 4 cm）。输出 /tmp/sp2/cover_<tag>_<语言>.png；tag 写在 /tmp/sp2/cover_tag.txt
var tag = System.IO.File.Exists("/tmp/sp2/cover_tag.txt") ? System.IO.File.ReadAllText("/tmp/sp2/cover_tag.txt").Trim() : "x";
var cov = GameObject.Find("Cover"); var mc = cov.GetComponent<WarTable.Game.MenuCover>();
var board = Object.FindObjectsByType<Renderer>().Where(r => r.enabled && !r.transform.IsChildOf(cov.transform)).ToList();
foreach (var r in board) r.enabled = false;
var sb = new System.Text.StringBuilder();
try {
  var eye = cov.transform.TransformPoint(new Vector3(0, 0.04f, 0.8f));
  foreach (var zh in new[] { true, false }) {
    foreach (var it in mc.items) { if (it.zh) it.zh.SetActive(zh); if (it.en) it.en.SetActive(!zh); }
    WarTable.EditorTools.WTCapture.Shot($"/tmp/sp2/cover_{tag}_{(zh ? "zh" : "en")}.png", eye, cov.transform.TransformPoint(new Vector3(0, -0.08f, 0)), 58f, 1200, 1200, new Color(0.12f, 0.12f, 0.13f));
  }
  foreach (var it in mc.items) {
    if (it.page != 0) continue;
    var p = it.root.position - eye; var l = cov.transform.InverseTransformDirection(p);
    sb.AppendLine($"{it.action}: 水平 {Mathf.Atan2(-l.x, -l.z) * Mathf.Rad2Deg:F1}° 垂直 {Mathf.Atan2(l.y, -l.z) * Mathf.Rad2Deg:F1}°");
  }
} finally { foreach (var r in board) r.enabled = true; foreach (var it in mc.items) { bool z = WarTable.Loc.Current == WarTable.Loc.Lang.Zh; if (it.zh) it.zh.SetActive(z); if (it.en) it.en.SetActive(!z); } }
result.Log(sb.ToString());
