#!/bin/bash
# Play 模式下把新手教学从头走一遍并逐步截图（/tmp/wt_fix/t_*.png）。先进 Play、开局后再跑。
T=~/.unity/wartable-tools/cs.py
mkdir -p /tmp/wt_fix
shot(){ python3 $T "var g = Object.FindAnyObjectByType<WarTable.Game.WarGame>(); var c = g.previewCamera.transform; WarTable.EditorTools.WTCapture.Shot(\"/tmp/wt_fix/t_$1.png\", c.position, c.position + c.forward, 90f, 1400, 1100); result.Log(\"$1 now=\" + g.Now + \" step=\" + g.tutorial.Now + \" pauseBtn=\" + (g.pause.button.gameObject.activeSelf ? g.pause.button.position.ToString(\"F2\") : \"off\"));" 2>&1 | grep -E "Log|rror"; }
run(){ python3 $T "var g = Object.FindAnyObjectByType<WarTable.Game.WarGame>(); $1 result.Log(\"step=\" + g.tutorial.Now + \" now=\" + g.Now + \" paused=\" + g.Paused);" 2>&1 | grep -E "Log|rror"; }
sleep 1; shot goal
sleep 3.5; shot select
run 'g.Command.Exec("sel:inf"); g.tutorial.OnAction("select");'
sleep 1.2; shot line
run 'g.tutorial.OnAction("line");'
sleep 1.2; shot fight
run 'g.Command.Exec("fight");'
sleep 1.2; shot attack
run 'g.Command.Exec("attack:inf");'
sleep 1.2; shot charge
run 'g.gestureFlash.Show(WarTable.XR.Gesture.Charge, true); g.Command.Exec("charge");'
sleep 0.25; shot flash
sleep 1; shot hold
run 'g.Command.Exec("hold"); g.Command.Exec("retreat");'
sleep 1.2; shot pause
run 'g.pause.SendMessage("Toggle");'
sleep 0.6; shot pausemenu
run 'g.pause.SendMessage("Toggle");'
sleep 1; shot finish
