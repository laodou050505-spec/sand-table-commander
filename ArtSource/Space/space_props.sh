#!/bin/bash
# 深空的 22 件武器 / 盾 + 2 辆坐骑：等定妆图出完（图生图并发很小，别抢）→ 原型的 generate.mjs（文字 → P2 模型）。密钥只放环境变量，不打印
until grep -q REFS18 /tmp/wartable-logs/space_refs.log 2>/dev/null; do sleep 15; done
cd ~/sandtable-xr
K=$(python3 -c "import sys; sys.path.insert(0, '/Users/ld/.codex/skills/tripo-image/scripts'); import tripo_image as a; print(a.api_key())")
IDS=$(node -e "import('./tools/tripo/assets.mjs').then(m=>console.log(Object.entries(m.ASSETS).filter(([k,v])=>v.spaceProp).map(([k])=>k).join(' ')))")
for round in 1 2 3 4 5 6; do
  TRIPO_API_KEY="$K" NODE_USE_ENV_PROXY=1 HTTPS_PROXY=http://127.0.0.1:7897 HTTP_PROXY=http://127.0.0.1:7897 node tools/tripo/generate.mjs $IDS >> /tmp/wartable-logs/space_props.log 2>&1
  miss=0; for i in $IDS; do [ -f tools/tripo/raw/$i/model.glb ] || miss=$((miss+1)); done
  echo "$(date +%H:%M:%S) round $round missing=$miss" >> /tmp/wartable-logs/space_props.log
  [ "$miss" = "0" ] && break
  sleep 30
done
echo "PROPS DONE $(date +%H:%M:%S)" >> /tmp/wartable-logs/space_props.log
