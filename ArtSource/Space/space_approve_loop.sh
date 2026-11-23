#!/bin/bash
# gothic_chars.py run 在还有兵种排队（429）时不会退出，一轮里放行不了已经审计完的白模 → 单独每 40 秒放行一次，直到 18 个都做完
until grep -q "ALL DONE" /tmp/wartable-logs/space_chars.log 2>/dev/null; do
  /Library/Developer/CommandLineTools/usr/bin/python3 /tmp/sp2/space_autoapprove.py >> /tmp/wartable-logs/space_approve.log 2>&1
  sleep 40
done
