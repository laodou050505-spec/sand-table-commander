#!/bin/bash
cd /Users/ld/Documents/ChatGPT/WarTable
PY=/Library/Developer/CommandLineTools/usr/bin/python3
until grep -q REFS18 /tmp/wartable-logs/space_refs.log 2>/dev/null; do sleep 15; done
HTTPS_PROXY=http://127.0.0.1:7897 $PY Tools/space_ui_gen.py >> /tmp/wartable-logs/space_ui_gen.log 2>&1
HTTPS_PROXY=http://127.0.0.1:7897 $PY Tools/space_ground_gen.py >> /tmp/wartable-logs/space_ground_gen.log 2>&1
for i in 1 2 3; do HTTPS_PROXY=http://127.0.0.1:7897 $PY Tools/space_scene_refs.py >> /tmp/wartable-logs/space_scene_refs.log 2>&1; done
echo "IMAGES DONE $(date +%H:%M:%S)" >> /tmp/wartable-logs/space_scene_refs.log
