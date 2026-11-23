#!/bin/bash
# 打开 PICO 手部 / 眼动 / 透视后，逐秒看 PXR_ProjectSetting.asset 何时被改回 0，并打印这段时间的编辑器日志。
cd /Users/ld/Documents/ChatGPT/WarTable
F=Assets/Resources/PXR_ProjectSetting.asset
L=$(wc -l < /tmp/wartable-logs/editor.log)
python3 ~/.unity/wartable-tools/cs.py 'result.Log(WarTable.EditorTools.WTBuild.EnsurePico());' 2>&1 | grep -E "Log|rror"
for i in $(seq 1 40); do
  v=$(grep -E "^  handTracking:" $F | awk '{print $2}')
  g=$(grep guid $F.meta | awk '{print $2}')
  echo "$(date +%T) hand=$v guid=${g:0:8} mtime=$(stat -f %Sm -t %T $F)"
  [ "$v" = "0" ] && break
  sleep 1
done
echo "--- log since set"
tail -n +$L /tmp/wartable-logs/editor.log | grep -iE "PXR|Resources|import|Refresh|domain|reload|compil" | grep -v Shader | head -20 | cut -c1-200
