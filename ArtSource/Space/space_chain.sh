#!/bin/bash
# 深空风格的总驱动（11-13）：等 18 张定妆图 → 兵种建模（gothic_chars.py，WT_ERA=space，自动放行干净的白模）
# 武器 / 盾 / 坐骑由另一个进程跑原型的 generate.mjs（见 space_props.sh）。日志 /tmp/wartable-logs/space_chars.log
cd /Users/ld/Documents/ChatGPT/WarTable
PY=/Library/Developer/CommandLineTools/usr/bin/python3; LOG=/tmp/wartable-logs/space_chars.log
ALL="kd_footman kd_guard kd_spearman kd_halberdier kd_swordsman kd_hammer kd_archer kd_crossbow kd_knight cl_warrior cl_guardian cl_skirmisher cl_pikeman cl_berserker cl_crusher cl_hunter cl_wolfrider cl_wolfarcher"
until grep -q REFS18 /tmp/wartable-logs/space_refs.log 2>/dev/null; do sleep 15; done
echo "$(date +%H:%M:%S) 定妆图齐了，开始建模" >> $LOG
for round in $(seq 1 400); do
  $PY /tmp/sp2/space_autoapprove.py >> $LOG 2>&1
  WT_ERA=space HTTPS_PROXY=http://127.0.0.1:7897 $PY Tools/gothic_chars.py run $ALL >> $LOG 2>&1
  n=$(for c in $ALL; do grep -l '"done": true' ArtSource/Space/chars/$c/state.json 2>/dev/null; done | wc -l | tr -d ' ')
  echo "$(date +%H:%M:%S) round $round done=$n" >> $LOG
  [ "$n" = "18" ] && break
  sleep 45
done
echo "ALL DONE $(date +%H:%M:%S)" >> $LOG
