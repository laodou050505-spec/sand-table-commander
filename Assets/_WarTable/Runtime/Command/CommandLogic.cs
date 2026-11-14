using System;
using System.Collections.Generic;

namespace WarTable.Command
{
    /// <summary>
    /// 指挥的纯逻辑（照原型 src/mr/commandLogic.ts 逐行移植，不依赖 Unity）：捏合状态机、划线换算成军令、布阵方块的摆放、结算评级。
    /// 坐标都是模拟层的码（原型坐标：Unity x = −x）；朝向约定和模拟层相同：yaw 0 面朝 −z，前方 (−sin, −cos)，右方 (cos, −sin)。
    /// </summary>
    public enum Grp { Inf, Arch, Cav, Ha }

    public struct P2
    {
        public double x, z;
        public P2(double x, double z) { this.x = x; this.z = z; }
    }

    /// <summary>DragTo：捏住自己的一支部队拖到某处松手（target = 那支部队，x / z = 松手处）。</summary>
    public enum PinchKind { Target, Clear, Move, Line, DragTo }

    /// <summary>捏下那一刻视线落在什么上：己方部队、敌方部队、我方帅旗（全军 / 开战）。</summary>
    public enum TargetKind { Own, Foe, Standard }

    public struct PinchTarget
    {
        public TargetKind kind; public Grp group;
        public PinchTarget(TargetKind kind, Grp group = Grp.Inf) { this.kind = kind; this.group = group; }
    }

    public struct PinchEvent
    {
        public PinchKind type;
        public PinchTarget target;
        public double x, z, x0, z0, x1, z1;
    }

    /// <summary>
    /// 一次捏合的解释：捏下时视线在某个目标上（己方部队 / 敌方部队 / 我方帅旗）→ 交给指挥层处理；捏下时视线不在沙盘上 → 取消全部选中；
    /// 在沙盘上捏下：不拖 = 移动到该点，拖动 = 划线列阵（松手才下令，途中只显示预览）。
    /// 头显版（10-31 用户：不要像电脑上那么繁琐）只靠这一个状态机下令，没有菜单。
    /// </summary>
    public class PinchCommand
    {
        /// <summary>拖动超过这么多码才算划线，否则算点一下。</summary>
        public const double ClickYards = 5;

        enum Mode { Idle, Flag, Draw, Outside, Drag }
        Mode mode = Mode.Idle;
        public P2? Start, End;
        /// <summary>
        /// 已有选中时捏在部队上：先不判定，松手时没拖开（&lt; ClickYards）才算点了这个目标，拖开了就是划线——
        /// 选中步兵后从它脚下往前拖一条线，不会被当成“再捏一次 = 取消选中”。
        /// </summary>
        PinchTarget? pending;

        /// <summary>正在划线时的预览（够长才有）。</summary>
        public (P2 start, P2 end)? Preview
        {
            get
            {
                if (mode != Mode.Draw || Start == null || End == null) return null;
                var s = Start.Value; var e = End.Value;
                return Hypot(e.x - s.x, e.z - s.z) >= ClickYards ? (s, e) : null;
            }
        }

        /// <summary>正在拖一支部队（11-07 用户：用手直接把部队拖到某处 / 某支敌军上）：拖开够远才有，界面画箭头。</summary>
        public (PinchTarget unit, P2 start, P2 end)? DragPreview
        {
            get
            {
                if (mode != Mode.Drag || pending == null || Start == null || End == null) return null;
                var s = Start.Value; var e = End.Value;
                return Hypot(e.x - s.x, e.z - s.z) >= ClickYards ? (pending.Value, s, e) : null;
            }
        }

        /// <summary>正捏住哪支部队、手现在落在哪（不管拖了多远）：界面层让那支部队的旗牌直接跟着手走（11-08 用户：直接用手把旗牌拖过去）。</summary>
        public (PinchTarget unit, P2 end)? Holding => mode == Mode.Drag && pending != null && End != null ? (pending.Value, End.Value) : null;

        /// <summary>比如第二只手也捏上了（变成抓托盘）：这一笔作废，松手前都不再响应。</summary>
        public void Cancel()
        {
            if (mode != Mode.Idle) mode = Mode.Outside;
            Start = End = null; pending = null;
        }

        /// <summary>正在划线（界面层据此放慢战场、画预览）。</summary>
        public bool Drawing => mode == Mode.Draw || mode == Mode.Drag;

        public PinchEvent? Update(bool pinching, PinchTarget? target, P2? point, bool hasSelection)
        {
            if (mode == Mode.Idle)
            {
                if (!pinching) return null;
                if (target != null)
                {
                    // 11-07：捏在自己的部队上 → 先不判定：松手时没拖开 = 点了它（选中 / 取消），拖开了 = 把它拖过去（走过去 / 打松手处的敌军）
                    if (target.Value.kind == TargetKind.Own && point != null)
                    {
                        mode = Mode.Drag; pending = target;
                        Start = End = point;
                        return null;
                    }
                    // 捏在敌军上直接算“打它”；捏帅旗 = 开战 / 全选
                    mode = Mode.Flag; return new PinchEvent { type = PinchKind.Target, target = target.Value };
                }
                if (point == null) { mode = Mode.Outside; return new PinchEvent { type = PinchKind.Clear }; }
                // 选中后捏空地：拖一条线 = 沿线列阵，点一下 = 走过去
                mode = hasSelection ? Mode.Draw : Mode.Outside;
                Start = End = point;
                return null;
            }
            if (pinching)
            {
                if ((mode == Mode.Draw || mode == Mode.Drag) && point != null) End = point;
                return null;
            }
            var m = mode; var st = Start; var en = End; var pt = pending;
            mode = Mode.Idle;
            Start = End = null; pending = null;
            if ((m != Mode.Draw && m != Mode.Drag) || st == null || en == null) return null;
            var s = st.Value; var e = en.Value;
            bool click = Hypot(e.x - s.x, e.z - s.z) < ClickYards;
            if (m == Mode.Drag)
                return click ? new PinchEvent { type = PinchKind.Target, target = pt!.Value }
                             : new PinchEvent { type = PinchKind.DragTo, target = pt!.Value, x = e.x, z = e.z };
            return click
                ? new PinchEvent { type = PinchKind.Move, x = e.x, z = e.z }
                : new PinchEvent { type = PinchKind.Line, x0 = s.x, z0 = s.z, x1 = e.x, z1 = e.z };
        }

        static double Hypot(double a, double b) => Math.Sqrt(a * a + b * b);
    }

    public struct LineOrder { public double x, z, yaw, length; }

    public static class CommandLogic
    {
        /// <summary>朝向 → yaw（前方 = (−sin, −cos)）。</summary>
        public static double YawToward(double dx, double dz) => Math.Atan2(-dx, -dz);

        /// <summary>一条线 → 站位：线的中点、线长，面朝垂直于线且偏向敌军的一侧。</summary>
        public static LineOrder Line(double x0, double z0, double x1, double z1, P2 enemy)
        {
            double dx = x1 - x0, dz = z1 - z0, length = Math.Sqrt(dx * dx + dz * dz);
            if (length == 0) length = 1e-3;
            double x = (x0 + x1) / 2, z = (z0 + z1) / 2;
            double nx = -dz / length, nz = dx / length;
            if (nx * (enemy.x - x) + nz * (enemy.z - z) < 0) { nx = -nx; nz = -nz; }
            return new LineOrder { x = x, z = z, yaw = YawToward(nx, nz), length = length };
        }

        /// <summary>选中了几支部队时把线平分给它们，各站一段。</summary>
        public static LineOrder[] SplitLine(LineOrder o, int n)
        {
            double rx = Math.Cos(o.yaw), rz = -Math.Sin(o.yaw);
            double seg = o.length / n;
            var r = new LineOrder[n];
            for (int i = 0; i < n; i++)
            {
                double off = (i - (n - 1) / 2.0) * seg;
                r[i] = new LineOrder { x = o.x + rx * off, z = o.z + rz * off, yaw = o.yaw, length = seg };
            }
            return r;
        }

        /// <summary>模拟层的间距上下限（AI.setSpacing 夹在这个范围）。</summary>
        const double SpacingMin = 0.6, SpacingMax = 2;

        /// <summary>想要的横队宽度 → 传给 setSpacing 的倍率。模拟层只能调间距，所以宽度有上下限。</summary>
        public static double SpacingFactor(double wanted, double currentWidth, double currentSpacing)
        {
            double target = Math.Max(SpacingMin, Math.Min(SpacingMax, currentSpacing * (wanted / Math.Max(3, currentWidth))));
            return target / currentSpacing;
        }

        /// <summary>布阵时用到的方块字段（deploy.ts 的 BlockPlan 的子集）。</summary>
        public interface IBlock { double X { get; set; } double Z { get; set; } double Yaw { get; set; } int Files { get; set; } }

        /// <summary>
        /// 布阵：把一支部队的所有方块整体搬到新位置。方块之间的相对位置保持（绕新朝向转过去），
        /// 横向按 widthScale 拉伸，每排人数跟着变。超出己方半场由调用方夹回来。
        /// </summary>
        public static void MoveBlocks(IList<IBlock> blocks, P2 to, double yaw, double widthScale)
        {
            if (blocks.Count == 0) return;
            double cx = 0, cz = 0;
            foreach (var b in blocks) { cx += b.X; cz += b.Z; }
            cx /= blocks.Count;
            cz /= blocks.Count;
            double y0 = blocks[0].Yaw;
            double r0x = Math.Cos(y0), r0z = -Math.Sin(y0), f0x = -Math.Sin(y0), f0z = -Math.Cos(y0);
            double r1x = Math.Cos(yaw), r1z = -Math.Sin(yaw), f1x = -Math.Sin(yaw), f1z = -Math.Cos(yaw);
            foreach (var b in blocks)
            {
                double dx = b.X - cx, dz = b.Z - cz;
                double r = (dx * r0x + dz * r0z) * widthScale, f = dx * f0x + dz * f0z;
                b.X = to.x + r1x * r + f1x * f;
                b.Z = to.z + r1z * r + f1z * f;
                b.Yaw = yaw;
                // JS Math.round：.5 向上取整（C# Math.Round 默认银行家舍入，不能直接用）
                if (b.Files > 0) b.Files = Math.Max(1, (int)Math.Floor(b.Files * widthScale + 0.5));
            }
        }

        /// <summary>结算评级：按己方存活比例给星。</summary>
        public static int Rating(bool won, int survivors, int total)
        {
            if (!won) return 0;
            double k = (double)survivors / Math.Max(1, total);
            return k >= 0.5 ? 3 : k >= 0.25 ? 2 : 1;
        }
    }
}
