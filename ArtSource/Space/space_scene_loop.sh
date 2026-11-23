#!/bin/bash
# 深空布景 15 件的 P2 驱动：等参考图出完 → 反复 run（白模出来就放行；成品之后和原始渲染图并排人工看）→ 全部 downloaded 后直接收尾（specs 里 src=raw，不走清理）
cd /Users/ld/Documents/ChatGPT/WarTable
B=/Applications/Blender.app/Contents/MacOS/Blender; LOG=/tmp/wartable-logs/space_scene_p2.log
ALL="S_Command S_Hab S_Tower S_Pod S_Hauler S_Crates S_Barrier S_Lamp S_Wreck S_Ribs S_Derrick S_Shack S_ScrapWall S_OreCart S_Spire"
until grep -q "IMAGES DONE" /tmp/wartable-logs/space_scene_refs.log 2>/dev/null; do sleep 20; done
for round in $(seq 1 200); do
  HTTPS_PROXY=http://127.0.0.1:7897 $B -b -P Tools/p2_pipeline.py -- run $ALL >> $LOG 2>&1
  WAIT=$(python3 ~/.unity/wartable-tools/p2status.py $LOG | tail -n +2 | grep "await-approval" | awk '{print $1}' | tr -d '":,{}' | tr '\n' ' ')
  [ -n "$WAIT" ] && HTTPS_PROXY=http://127.0.0.1:7897 $B -b -P Tools/p2_pipeline.py -- approve $WAIT >> $LOG 2>&1
  n=0; for s in $ALL; do [ -f ArtSource/ModelsP2/$s/model.glb ] && n=$((n+1)); done
  echo "$(date +%H:%M:%S) round $round textured=$n approved_now=[$WAIT]" >> $LOG
  [ "$n" = "15" ] && break
  sleep 40
done
python3 -I Tools/p2_finalize.py $ALL > /tmp/wartable-logs/space_scene_fin.out 2>&1
echo "SCENE DONE $(date +%H:%M:%S)" >> $LOG
