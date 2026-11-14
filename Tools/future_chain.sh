#!/bin/bash
# 未来时代兵种的加工链（11-12）：原型补 50 段动作 → 转 FBX → 做 LOD1。
# 用法：bash Tools/future_chain.sh cl_berserker kd_halberdier …   （传不带前缀的兵种名；脚本自己加 f_，并检查 raw/f_<兵种>/ 齐全）
# 11-13：现代时代同一条链——ERA_PRE=m_ bash Tools/future_chain.sh …（前缀换成 m_）
# 11-12 教训：在 zsh 里用 `for n in $VAR` 不会按空格拆开，曾把中古版兵种名传给转换脚本、覆盖了中古模型（已从 git 恢复）。
# 所以这里用 bash、参数逐个处理，并且拒绝任何不以 kd_ / cl_ 开头的名字，传给下游的名字一律带 f_。
set -u
PRE=${ERA_PRE:-f_}
PROTO=~/sandtable-xr; PROJ=/Users/ld/Documents/ChatGPT/WarTable; B=/Applications/Blender.app/Contents/MacOS/Blender
NAMES=()
for n in "$@"; do
  case "$n" in kd_*|cl_*) ;; *) echo "拒绝：$n（要传 kd_ / cl_ 开头的兵种名）"; exit 2;; esac
  for f in model.glb rig.glb anim0.glb anim1.glb anim2.glb anim3.glb; do [ -f "$PROTO/tools/tripo/raw/$PRE$n/$f" ] || { echo "缺 raw/$PRE$n/$f"; exit 3; }; done
  NAMES+=("$PRE$n")
done
[ ${#NAMES[@]} -gt 0 ] || { echo "没有兵种"; exit 1; }
echo "加工：${NAMES[*]}"
LOG=/tmp/wartable-logs/future_chain_$(date +%H%M%S).log
(cd "$PROTO" && npx tsx tools/build/build.ts "${NAMES[@]}") > "$LOG" 2>&1 || { echo "build 失败，见 $LOG"; exit 4; }
grep -E "^$PRE" "$LOG" | cut -c1-100
"$B" -b -P "$PROJ/Tools/convert_demo_assets.py" -- "${NAMES[@]}" >> "$LOG" 2>&1
"$B" -b -P "$PROJ/Tools/make_demo_lods.py" -- 0.3 "${NAMES[@]}" >> "$LOG" 2>&1
grep -E "^OK |^LOD1 |debris|Traceback" "$LOG" | cut -c1-110
grep -E "^(OK|LOD1) troops (kd_|cl_)" "$LOG" && { echo "！！出现了不带时代前缀的名字，检查 $LOG"; exit 5; }
echo "完成 ${#NAMES[@]} 个"
