#!/bin/bash
# 封面 v5（墨金书法）的立体字：每个书法单字 / 英文行单独挤出成一个 FBX（Unity 里自己排错位版式、前后错开）。
# 用法：Tools/cover_v5_meshes.sh <名称:字形图:分组数:厚度:倒角> ...
#   书法字：厚 0.06、倒角 0.008（相对字宽 1）；英文小字：厚 0.012、倒角 0.0015。
cd "$(dirname "$0")/.."
T=$PWD
OUT=${COVER_OUT:-Assets/_WarTable/Art/Models/Title/v5}
mkdir -p "$OUT" /tmp/cover_v5
for spec in "$@"; do
  IFS=: read name img n depth bev <<< "$spec"
  python3 "$T/Tools/title_contours.py" "$T/$img" "/tmp/cover_v5/$name.json" "$n" ${ROPEN:-4} || exit 1
  /Applications/Blender.app/Contents/MacOS/Blender -b -P "$T/Tools/blender_title3d.py" -- "/tmp/cover_v5/$name.json" "$T/$OUT/$name.fbx" "$depth" "$bev" 2>&1 | grep -E "导出|Error|Traceback"
done
