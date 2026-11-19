// 自动生成：由 sandtable-xr/src/sim/formation.ts 忠实移植
using System;
using System.Collections.Generic;

namespace WarTable.Sim
{
    /// <summary>
    /// Bannerlord 风格的阵型：每名成员相对阵型锚点和朝向的站位。
    /// Slot 0.. 是前排/外圈优先，按持盾能力排序后放在最合适的位置。
    /// </summary>

    /// <summary>一个成员的站位槽。</summary>
    public struct Slot
    {
        public double X;
        public double Z;
        /// <summary>该槽成员的朝向（圆阵/方阵中朝外）。</summary>
        public double Yaw;
        /// <summary>true 表示前排或外圈（直接接敌的位置）。</summary>
        public bool Front;
    }

    /// <summary>阵型种类枚举（PascalCase，对应 TS 字符串 key）。</summary>
    public enum Arrangement
    {
        Line,       // line       横队
        Shieldwall, // shieldwall 盾墙
        Loose,      // loose      散阵
        Circle,     // circle     圆阵
        Square,     // square     方阵
        Skein,      // skein      楔形
        Column      // column     纵队
    }

    public static class FormationData
    {
        public static readonly Arrangement[] Arrangements =
        {
            Arrangement.Line, Arrangement.Shieldwall, Arrangement.Loose,
            Arrangement.Circle, Arrangement.Square, Arrangement.Skein, Arrangement.Column
        };

        /// <summary>阵型中文名称。</summary>
        public static readonly Dictionary<Arrangement, string> ArrangementNames = new()
        {
            { Arrangement.Line,       "横队" },
            { Arrangement.Shieldwall, "盾墙" },
            { Arrangement.Loose,      "散阵" },
            { Arrangement.Circle,     "圆阵" },
            { Arrangement.Square,     "方阵" },
            { Arrangement.Skein,      "楔形" },
            { Arrangement.Column,     "纵队" },
        };

        /// <summary>附近伤亡对士气的影响乘数（越小越稳）。</summary>
        public static readonly Dictionary<Arrangement, double> ArrangementMorale = new()
        {
            { Arrangement.Line,       1.0  },
            { Arrangement.Shieldwall, 0.75 },
            { Arrangement.Loose,      1.0  },
            { Arrangement.Circle,     1.0  },
            { Arrangement.Square,     0.75 },
            { Arrangement.Skein,      0.75 },
            { Arrangement.Column,     0.75 },
        };

        /// <summary>保持阵型移动时的速度倍率。</summary>
        public static readonly Dictionary<Arrangement, double> ArrangementPace = new()
        {
            { Arrangement.Line,       1.0  },
            { Arrangement.Shieldwall, 0.7  },
            { Arrangement.Loose,      1.0  },
            { Arrangement.Circle,     0.6  },
            { Arrangement.Square,     0.6  },
            { Arrangement.Skein,      1.0  },
            { Arrangement.Column,     1.1  },
        };
    }

    public static class Formation
    {
        // 前向分量：yaw 轴朝 −Z 时 forward = (−sin, −cos)
        private static double Fx(double yaw) => -Math.Sin(yaw);
        private static double Fz(double yaw) => -Math.Cos(yaw);

        /// <summary>
        /// 为 n 人生成阵型槽，锚点 (x,z)，朝向 yaw，间距 spacing（米/码）。
        /// cavalry 控制纵深，files 指定每排人数（0=自动）。
        /// 返回列表中 Slot 0.. 为前排优先。
        /// </summary>
        public static List<Slot> Arrange(Arrangement kind, int n, double x, double z,
            double yaw, double spacing, bool cavalry, int files = 0)
        {
            var out_ = new List<Slot>(n);
            if (n == 0) return out_;

            // 右方向向量
            double rx = Math.Cos(yaw);
            double rz = -Math.Sin(yaw);
            // 前方向向量
            double fx_ = Fx(yaw);
            double fz_ = Fz(yaw);

            // 骑兵纵深更大
            double depth = cavalry ? 4.5 : 1.9;

            // 辅助：在 (side*right + fwd*forward) 偏移处放一个槽
            void Put(double side, double fwd, double faceYaw, bool front)
            {
                out_.Add(new Slot
                {
                    X = x + rx * side + fx_ * fwd,
                    Z = z + rz * side + fz_ * fwd,
                    Yaw = faceYaw,
                    Front = front
                });
            }

            switch (kind)
            {
                case Arrangement.Line:
                case Arrangement.Shieldwall:
                case Arrangement.Loose:
                {
                    double gap = spacing * (kind == Arrangement.Shieldwall ? 0.62 : kind == Arrangement.Loose ? 2.0 : 1.0);
                    double rowGap = depth * (kind == Arrangement.Shieldwall ? 0.7 : kind == Arrangement.Loose ? 1.6 : 1.0);
                    // 约为宽 4 倍于深：每排约 √(4n) 人
                    int perRow = files > 0
                        ? Math.Max(1, Math.Min(n, files))
                        : Math.Max(4, Math.Min(n, (int)Math.Ceiling(Math.Sqrt(n * 4.0))));
                    for (int i = 0; i < n; i++)
                    {
                        int row = i / perRow;
                        int inRow = Math.Min(perRow, n - row * perRow);
                        Put((i % perRow - (inRow - 1) * 0.5) * gap, -row * rowGap, yaw, row == 0);
                    }
                    break;
                }

                case Arrangement.Column:
                {
                    int cols = files > 0
                        ? Math.Max(1, Math.Min(files, n))
                        : Math.Max(2, Math.Min(4, (int)Math.Ceiling(n / 8.0)));
                    for (int i = 0; i < n; i++)
                    {
                        int row = i / cols;
                        Put((i % cols - (cols - 1) * 0.5) * spacing * 0.8, -row * depth * 0.8, yaw, row == 0);
                    }
                    break;
                }

                case Arrangement.Skein:
                {
                    // 楔形：顶点一人，每排向两侧各扩一人
                    int i = 0;
                    for (int rank = 0; i < n; rank++)
                    {
                        int width = rank * 2 + 1;
                        for (int k = 0; k < width && i < n; k++, i++)
                        {
                            double side = (k - rank) * spacing * 0.9;
                            Put(side, -rank * depth * 0.75, yaw, k == 0 || k == width - 1 || rank == 0);
                        }
                    }
                    break;
                }

                case Arrangement.Circle:
                {
                    // 从外圈向内圈，每圈朝外
                    int left = n;
                    double r = Math.Max(2.0, Math.Min(n, 24) * spacing / (2 * Math.PI));
                    bool first = true;
                    while (left > 0)
                    {
                        int fit = Math.Max(1, Math.Min(left, (int)Math.Floor(2 * Math.PI * r / spacing)));
                        for (int k = 0; k < fit; k++)
                        {
                            double a = yaw + (k / (double)fit) * Math.PI * 2;
                            double px = -Math.Sin(a) * r;
                            double pz = -Math.Cos(a) * r;
                            out_.Add(new Slot { X = x + px, Z = z + pz, Yaw = a, Front = first });
                        }
                        left -= fit;
                        r -= depth * 0.8;
                        first = false;
                        if (r < 1.0) r = 1.0;
                    }
                    break;
                }

                case Arrangement.Square:
                {
                    // 四面中空方阵，每面朝外；剩余人员填充内圈
                    int left = n;
                    double half = Math.Max(2.0, Math.Ceiling(Math.Min(n, 40) / 4.0) * spacing / 2.0);
                    bool first = true;
                    while (left > 0)
                    {
                        int perSide = Math.Max(1, Math.Min((int)Math.Ceiling(left / 4.0),
                            (int)Math.Floor(half * 2 / spacing)));
                        for (int s = 0; s < 4 && left > 0; s++)
                        {
                            double faceYaw = yaw + s * Math.PI / 2;
                            double frx = Math.Cos(faceYaw);
                            double frz = -Math.Sin(faceYaw);
                            double ffx = Fx(faceYaw);
                            double ffz = Fz(faceYaw);
                            for (int k = 0; k < perSide && left > 0; k++, left--)
                            {
                                double side = (k - (perSide - 1) * 0.5) * spacing;
                                out_.Add(new Slot
                                {
                                    X = x + ffx * half + frx * side,
                                    Z = z + ffz * half + frz * side,
                                    Yaw = faceYaw,
                                    Front = first
                                });
                            }
                        }
                        half -= depth * 0.8;
                        first = false;
                        if (half < 1.0) half = 1.0;
                    }
                    break;
                }
            }

            return out_;
        }
    }
}
