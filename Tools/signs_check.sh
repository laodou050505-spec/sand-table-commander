#!/bin/bash
# 重装配场景 → 进 Play → 开局截布阵 → 开战下令截交战，拼到 /tmp/wt_fix/s_pair.png（核对旗牌、去向箭头、说明牌、把手、暂停键）
cd /Users/ld/Documents/ChatGPT/WarTable
T=~/.unity/wartable-tools/cs.py
mkdir -p /tmp/wt_fix
python3 $T 'if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode(); result.Log("p=" + EditorApplication.isPlaying);' 2>&1 | grep Log
n=0; until python3 $T 'result.Log("p=" + EditorApplication.isPlaying);' 2>&1 | grep -q "p=False"; do sleep 1; n=$((n+1)); [ $n -gt 30 ] && break; done
python3 $T 'AssetDatabase.Refresh(); WarTable.EditorTools.GameSetup.Setup(); WarTable.Game.Tutorial.Seen = true; PlayerPrefs.Save(); UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes(); result.Log("setup");' 2>&1 | grep -E "Log|rror"
python3 $T 'EditorApplication.EnterPlaymode(); result.Log("enter");' 2>&1 | grep Log
n=0; until python3 $T 'result.Log("p=" + EditorApplication.isPlaying);' 2>&1 | grep -q "p=True"; do sleep 1; n=$((n+1)); [ $n -gt 60 ] && echo timeout && break; done
sleep 3
python3 $T 'Object.FindAnyObjectByType<WarTable.Game.MenuCover>().FireAction(WarTable.Game.MenuCover.Action.Begin); result.Log("begin");' 2>&1 | grep Log
sleep 2
python3 $T 'var g = Object.FindAnyObjectByType<WarTable.Game.WarGame>(); var c = g.previewCamera.transform; WarTable.EditorTools.WTCapture.Shot("/tmp/wt_fix/s_deploy.png", c.position, c.position + c.forward, 90f, 1400, 1100); g.Command.Exec("sel:inf"); g.Command.Exec("fight"); g.Command.Exec("attack:inf"); g.Command.Exec("sel:cav"); g.Command.Exec("charge"); result.Log("now=" + g.Now);' 2>&1 | grep -E "Log|rror"
sleep 2
python3 $T 'var g = Object.FindAnyObjectByType<WarTable.Game.WarGame>(); var c = g.previewCamera.transform; WarTable.EditorTools.WTCapture.Shot("/tmp/wt_fix/s_fight.png", c.position, c.position + c.forward, 90f, 1400, 1100); result.Log("sel=" + string.Join(",", g.Command.Selected) + " inf=" + g.Command.Intent("inf") + " cav=" + g.Command.Intent("cav"));' 2>&1 | grep -E "Log|rror"
python3 -c "
from PIL import Image
c=Image.new('RGB',(1400,550)); c.paste(Image.open('/tmp/wt_fix/s_deploy.png').resize((700,550)),(0,0)); c.paste(Image.open('/tmp/wt_fix/s_fight.png').resize((700,550)),(700,0)); c.save('/tmp/wt_fix/s_pair.png')"
