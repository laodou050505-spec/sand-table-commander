var g = Object.FindAnyObjectByType<WarTable.Game.WarGame>();
var T = typeof(WarTable.Game.Tutorial); var BF = (System.Reflection.BindingFlags)36;
var mode = System.IO.File.ReadAllText("/tmp/sp2/mode.txt").Trim();
if (mode == "begin") { var menu = Object.FindAnyObjectByType<WarTable.Game.MenuCover>(); typeof(WarTable.Game.MenuCover).GetMethod("Fire", (System.Reflection.BindingFlags)52).Invoke(menu, new object[]{ WarTable.Game.MenuCover.Action.Begin }); }
if (mode == "fight") g.Command.Exec("fight");
if (mode == "throw") {
  var R = typeof(WarTable.Game.OrderRack);
  var toks = (Transform[])R.GetField("tokens", BF).GetValue(g.orders);
  toks[0].position = g.board.TransformPoint(new Vector3(0, 0.12f, 0.1f));
  R.GetMethod("Throw", BF).Invoke(g.orders, new object[]{ 0, 1 });
}
result.Log($"{mode}: phase={g.Now} cur={T.GetField("cur", BF).GetValue(g.tutorial)} learning={T.GetField("learning", BF).GetValue(g.tutorial)} charge学会={WarTable.Game.Tutorial.Learned(WarTable.Game.Tutorial.Hint.Charge)} cmd={g.Battle?.Commander[0]},{g.Battle?.Commander[1]}");
