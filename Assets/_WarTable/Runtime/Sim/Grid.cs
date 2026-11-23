// 自动生成：由 sandtable-xr/src/sim/grid.ts 忠实移植
using System;
using System.Collections.Generic;

namespace WarTable.Sim
{
    /// <summary>
    /// 均匀空间哈希（地面 XZ 平面）：将 Agent 按格子归桶，
    /// 每次查询"附近有谁"只需扫描几个格子，而非遍历全场。
    /// 每帧调用 Build 重建（O(n) 一遍）。
    /// </summary>
    public class SpatialGrid
    {
        private const int CELL = 4;

        // key = (cx+1024)*4096 + (cz+1024)，int 足够覆盖 ±1024 个格子（±4096 码）
        private readonly Dictionary<int, List<Agent>> cells = new();
        // 对象池：避免频繁分配 List<Agent>
        private readonly List<List<Agent>> pool = new();
        // Near 每次清空复用，调用方如需保留须自行复制
        private readonly List<Agent> outList = new();

        private static int Key(int cx, int cz) => (cx + 1024) * 4096 + (cz + 1024);

        /// <summary>把所有存活且在场的 Agent 按格子归桶。</summary>
        public void Build(List<Agent> agents)
        {
            // 把旧格子列表归还到对象池（清空但不释放）
            foreach (var kv in cells)
            {
                kv.Value.Clear();
                pool.Add(kv.Value);
            }
            cells.Clear();

            foreach (var a in agents)
            {
                if (!a.Alive || a.Gone) continue;
                int k = Key((int)Math.Floor(a.X / CELL), (int)Math.Floor(a.Z / CELL));
                if (!cells.TryGetValue(k, out var list))
                {
                    // 优先复用对象池里的空列表
                    if (pool.Count > 0)
                    {
                        list = pool[pool.Count - 1];
                        pool.RemoveAt(pool.Count - 1);
                    }
                    else
                    {
                        list = new List<Agent>();
                    }
                    cells[k] = list;
                }
                list.Add(a);
            }
        }

        /// <summary>
        /// 返回所有格子中心距 (x,z) 在 r 以内的格子里的 Agent
        /// （是真实距离 ≤ r 的超集，调用方仍需做精确距离筛选）。
        /// 返回的列表在下一次 Near 调用前有效，如需持久保存请自行复制。
        /// </summary>
        public List<Agent> Near(double x, double z, double r)
        {
            var result = outList;
            result.Clear();
            int x0 = (int)Math.Floor((x - r) / CELL);
            int x1 = (int)Math.Floor((x + r) / CELL);
            int z0 = (int)Math.Floor((z - r) / CELL);
            int z1 = (int)Math.Floor((z + r) / CELL);
            // 按 cx/cz 嵌套循环，遍历顺序确定
            for (int cx = x0; cx <= x1; cx++)
            {
                for (int cz = z0; cz <= z1; cz++)
                {
                    if (cells.TryGetValue(Key(cx, cz), out var list))
                        result.AddRange(list);
                }
            }
            return result;
        }
    }
}
