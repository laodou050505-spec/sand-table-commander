using UnityEngine;
using WarTable.Sim;

namespace WarTable.Game
{
    /// <summary>
    /// 一枚棋子的显示（照原型 src/view/agentView.ts 的 update 逐段移植）：读模拟层的 Agent，摆位置朝向、
    /// 选上下半身动作（攻击 / 格挡等严格停在模拟给的片段时间）、换武器、管坐骑（骑着 / 跑散 / 倒地）。
    /// 坐标：模拟层沿用原型（码），Unity 沙盘本地 = (−x, y, z) × 1 码；朝向 Unity = 180° − yaw。
    /// </summary>
    public class FigureView
    {
        public readonly Agent Agent;
        public readonly GameObject Root;
        readonly FigureRig rig;
        readonly Transform figure, rider, mountRoot, mountT;
        readonly Vector3 riderRest;
        readonly string deathClip;
        readonly System.Func<float, float, (Vector3 pos, Vector3 up)> ground;

        float hip, hipTarget, leanYaw, leanPitch, deathShown, deadMountShown, corpseT, mountCorpseT;
        bool frozen, deadMountPlaced, mountWasLive;
        HoverMount hover;
        Quaternion spineLean = Quaternion.identity;

        /// <summary>赢的一方（打完后站着的人欢呼）。</summary>
        public static int? Victor;

        // 11-08 用户：撤退 / 溃逃时看不到溃逃的信号——溃兵整个人（连底座的队伍色圈）褪成灰白，一眼看出“这人崩了、不算兵力了”；
        // 重整（士气回升）就恢复原色。敌我一样。用 MaterialPropertyBlock，不改共享材质
        bool routShown;
        Renderer[] tintRs;
        static MaterialPropertyBlock routMpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly Color RoutBase = new(0.62f, 0.62f, 0.6f, 1f), RoutGlow = new(0.24f, 0.24f, 0.22f, 1f);

        void ShowRout(bool on)
        {
            routShown = on;
            if (tintRs == null)
            {
                var list = new System.Collections.Generic.List<Renderer>(Root.GetComponentsInChildren<Renderer>(true));
                if (mountRoot) list.AddRange(mountRoot.GetComponentsInChildren<Renderer>(true));
                tintRs = list.ToArray();
            }
            routMpb ??= new MaterialPropertyBlock();
            foreach (var r in tintRs)
            {
                if (!r) continue;
                if (!on) { r.SetPropertyBlock(null); continue; }
                routMpb.Clear(); routMpb.SetColor(BaseColorId, RoutBase); routMpb.SetColor(EmissionId, RoutGlow);
                r.SetPropertyBlock(routMpb);
            }
        }

        const float MaxHip = 1.3f, Backward = 1.9f;
        static readonly Vector3 SaddleHorse = new(0, 1.55f, 0.044f), SaddleWolf = new(0, 1.45f, 0.051f);

        public FigureView(Agent a, GameObject prefab, Transform parent, System.Func<float, float, (Vector3, Vector3)> ground)
        {
            Agent = a;
            this.ground = ground;
            Root = Object.Instantiate(prefab, parent, false);
            Root.name = $"{a.Troop.Id}_{a.Id}";
            rig = Root.GetComponent<FigureRig>();
            figure = rig.figure;
            rider = rig.rider.transform;
            riderRest = rider.localPosition;
            deathClip = a.Id % 2 == 0 ? "Death" : "DeathBack";
            // 棋子在显示层自己管 LOD 之外的事；预制体上的 LODGroup 保留（原模 / 低模）
            rig.rider.Init();
            if (rig.mount)
            {
                // 坐骑拆成独立对象：骑手阵亡 / 落马后坐骑自己跑散或倒在原地（原型 updateMount）
                mountT = rig.mount.transform;
                mountRoot = new GameObject(Root.name + "_mount").transform;
                mountRoot.SetParent(parent, false);
                var inner = new GameObject("Scale").transform;
                inner.SetParent(mountRoot, false);
                inner.localScale = figure.localScale;
                inner.localPosition = figure.localPosition;
                mountT.SetParent(inner, false);
                mountT.localPosition = Vector3.zero;
                mountT.localRotation = Quaternion.identity;
                rig.mount.Init();
                hover = mountT.GetComponentInChildren<HoverMount>(true);   // 未来时代的悬浮载具（没有骨骼动作，自己起伏）
            }
            Update(0);
        }

        static Vector3 ToLocal(double x, double y, double z) => new Vector3(-(float)x, (float)y, (float)z) * BoardMap.Yard;
        static Quaternion Yaw(double yaw) => Quaternion.Euler(0, 180f - (float)yaw * Mathf.Rad2Deg, 0);

        /// <summary>摆在地形网格上（不是高度函数：网格和函数差几毫米，坡上会半埋），底座顺坡、人物直立。</summary>
        void Place(Transform t, Transform upright, double x, double z, double yaw)
        {
            var (pos, up) = ground(-(float)x, (float)z);
            t.localPosition = pos;
            t.localRotation = Quaternion.FromToRotation(Vector3.up, up) * Yaw(yaw);
            if (upright) upright.rotation = Quaternion.FromToRotation(upright.up, t.parent.up) * upright.rotation;
        }

        public void Update(float dt)
        {
            var a = Agent;
            bool rout = a.Routed && a.Alive && !a.Gone;
            if (rout != routShown) ShowRout(rout);
            if (a.Gone)
            {
                if (Root.activeSelf) Root.SetActive(false);
                if (mountRoot && mountRoot.gameObject.activeSelf) mountRoot.gameObject.SetActive(false);
                return;
            }
            if (frozen)
            {
                // 阵亡的棋子：倒下后停一会儿，慢慢沉进地里撤下桌（真实桌游也把阵亡的棋子拿走；
                // 躺平的身子比底座长得多，留着会压到旁边活人的脚下、叠在别的尸体上 = 穿模）
                corpseT += dt;
                if (Sink(Root.transform, corpseT, dt)) Root.SetActive(false);
                UpdateMount(a, false, dt);
                return;
            }
            bool onHorse = AgentOps.Mounted(a);
            UpdateMount(a, onHorse, dt);

            // ── 位置（骑手坐在鞍点；原型 toWorld + SADDLE）
            Place(Root.transform, figure, a.X, a.Z, a.Yaw - hip);
            if (onHorse)
            {
                var s = a.Mount!.Kind == "wolf" ? SaddleWolf : SaddleHorse;
                rider.localPosition = hover ? s + new Vector3(0f, hover.Lift - hover.seatDrop, 0f) : s;   // 悬浮载具上下起伏，骑手跟着浮
            }
            else rider.localPosition = Vector3.zero;

            // ── 武器显示（换武器过半时已是新武器；盾只配单手 / 投掷 / 单手长杆）
            int shown = a.Action.Kind == "swap" && a.Action.T > 0.22 ? a.Action.To : a.WeaponIdx;
            var held = WeaponsData.Weapons[a.Troop.Weapons[shown]];
            rig.ShowWeapon(shown);
            rig.ShowShield(a.Troop.Shield != null && a.ShieldHp > 0 && (held.Cls == WeaponClass.OneHanded || held.Cls == WeaponClass.Thrown || held.OneHandedPolearm));

            // ── 动作（原型 agentView.update 的 switch）
            var w = AgentOps.WeaponOf(a);
            var set = Defs.GetClipSet(a.Troop.Model, w.Cls, w.OneHandedPolearm);
            bool rifle = w.Cls == WeaponClass.Crossbow || (w.Cls == WeaponClass.Bow && rig.rifleBows);   // 现代时代的“弓”是步枪
            string ready = rifle ? "ReadyRifle" : w.Cls == WeaponClass.Bow ? "ReadyBow" : set.Ready;
            var act = a.Action;
            var up = rig.rider.Upper; var low = rig.rider.Lower;
            string full = null; Dir? twist = null;
            switch (act.Kind)
            {
                case "dead": full = deathClip; break;
                case "attack":
                    up.Play(act.Clip, time: (float)act.T, fade: 0.08f);
                    if (!act.Released) twist = act.Dir;
                    break;
                case "block":
                    if (AgentOps.HasShield(a)) up.Play("ShieldBlock", time: 0.25f, fade: 0.08f);
                    else { up.Play(set.Block, time: 0.22f, fade: 0.08f); twist = act.Dir; }
                    break;
                case "stagger":
                    if (act.Heavy && !onHorse) full = "CombatCritical";
                    else up.Play("CombatWound", time: Mathf.Min(0.45f, (float)act.T), fade: 0.05f);
                    break;
                case "kick": full = "Kick"; break;
                case "bash": up.Play("ShieldBash", time: Mathf.Min(0.9f, (float)act.T * 1.5f), fade: 0.06f); break;
                case "down": full = "Knockdown"; break;
                case "draw":
                    if (w.Cls == WeaponClass.Thrown) up.Play(act.T < 0.3 ? "LoadThrown" : "HoldThrown", fade: 0.1f);
                    else if (rifle) up.Play("HoldRifle", fade: 0.1f);
                    else if (act.T < 0.5) up.Play("LoadBow", time: (float)act.T * 2f, fade: 0.1f);
                    else up.Play("HoldBow", fade: 0.15f);
                    break;
                case "reload": up.Play("LoadRifle", time: Mathf.Min(0.95f, (float)(act.T / (w.Reload ?? 3))), fade: 0.1f); break;
                case "release":
                    if (w.Cls == WeaponClass.Thrown) up.Play("AttackThrown", time: Mathf.Min((float)act.T * 1.4f, 0.6f), fade: 0.05f);
                    else if (rifle) up.Play("AttackRifle", time: Mathf.Min((float)act.T * 1.6f, 0.9f), fade: 0.05f);
                    else up.Play("AttackBow", time: Mathf.Min((float)act.T * 1.6f, 0.6f), fade: 0.05f);
                    break;
                case "swap": up.Play("Sheath", time: (float)(act.T / 0.45) * 0.9f, fade: 0.1f); break;
                case "couch": up.Play("Couch", fade: 0.2f); break;
                default:
                    if (a.Braced) { up.Play(set.Attack[Dir.Down], time: 0.2f, fade: 0.15f); break; }
                    up.Play(a.Routed ? (onHorse ? "Mount" : "Run") : ready);
                    break;
            }
            if (full == null && a.Alive && !onHorse && Victor == a.Team && act.Kind == "idle" && AgentOps.SpeedOf(a) < 0.5) full = "Victory";

            hipTarget = 0;
            if (full != null)
            {
                if (full == "Kick" && act.Kind == "kick") { up.Play(full, time: 0.15f + (float)act.T * 1.1f); low.Play(full, time: 0.15f + (float)act.T * 1.1f); }
                else if (full == "Knockdown" && act.Kind == "down") { float t = Mathf.Min(1.95f, (float)(act.T / act.Dur) * 2f); up.Play(full, time: t, fade: 0.08f); low.Play(full, time: t, fade: 0.08f); }
                else if (act.Kind == "dead") { up.Play(full, time: (float)act.T, fade: 0.12f); low.Play(full, time: (float)act.T, fade: 0.12f); }
                else if (full == "Victory") { up.Play(full, fade: 0.3f); low.Play(full, fade: 0.3f); }
                else { up.Play(full, once: full == "Kick", fade: 0.12f); low.Play(full, once: full == "Kick", fade: 0.12f); }
            }
            else if (onHorse) low.Play("Mount");
            else Locomotion(a, ready, up, low);

            // 髋部转向移动方向（整个人转过去，上半身下面再转回来）
            hip += (hipTarget - hip) * (1 - Mathf.Exp(-dt * 12));
            if (Mathf.Abs(hip) < 1e-3f && hipTarget == 0) hip = 0;

            rig.rider.Tick(dt);
            if (act.Kind == "dead") deathShown += dt;
            if (act.Kind == "dead" && act.T > 3 && deathShown > 0.4f) frozen = true;

            // 起手 / 格挡时躯干朝挥砍方向侧倾；上半身按髋部转回来（骑在马上时按骑手的视线转）
            if (rig.spine)
            {
                float yaw = twist == Dir.Left ? -0.45f : twist == Dir.Right ? 0.45f : 0;
                float pitch = twist == Dir.Up ? 0.25f : twist == Dir.Down ? -0.12f : 0;
                float k = 1 - Mathf.Exp(-dt * 14);
                leanYaw += (yaw - leanYaw) * k;
                leanPitch += (pitch - leanPitch) * k;
                float twistUp = onHorse ? (float)a.Look : hip;
                // 绕竖直方向转（世界的竖直轴，换到 Unity 镜像坐标：角度取反），再加侧倾俯仰
                var vertical = Quaternion.AngleAxis(-(twistUp + leanYaw) * Mathf.Rad2Deg, Root.transform.parent.up);
                var pitchQ = Quaternion.AngleAxis(-leanPitch * Mathf.Rad2Deg, figure.right);
                rig.spine.rotation = vertical * pitchQ * rig.spine.rotation;
            }
        }

        /// <summary>阵亡后：停 2 秒，再用 1.5 秒沉进地里（约 3 cm）；沉完返回 true。time 是阵亡后累计的（战斗）时间。</summary>
        static bool Sink(Transform t, float time, float dt)
        {
            const float Wait = 2f, Dur = 1.5f, Depth = 0.03f;
            if (time < Wait) return false;
            float k = Mathf.Clamp01((time - Wait) / Dur), prev = Mathf.Clamp01((time - dt - Wait) / Dur);
            t.localPosition -= Vector3.up * Depth * (k - prev);
            return k >= 1f;
        }

        /// <summary>腿：原型 gaitOf——慢于 0.35 码/秒站着；倒退超过 1.9 弧度走 Walkbackwards；否则走 / 跑，髋部最多转 1.3 弧度。</summary>
        void Locomotion(Agent a, string ready, FigureAnimator.Layer up, FigureAnimator.Layer low)
        {
            double sp = AgentOps.SpeedOf(a);
            if (sp < 0.35) { low.Play(a.Alive ? ready : "Death"); return; }
            double fwd = a.Vx * AgentOps.ForwardX(a.Yaw) + a.Vz * AgentOps.ForwardZ(a.Yaw);
            double side = a.Vx * System.Math.Cos(a.Yaw) - a.Vz * System.Math.Sin(a.Yaw);
            double heading = System.Math.Atan2(side, fwd);
            string clip; float rate, h;
            if (System.Math.Abs(heading) > Backward) { h = (float)(heading - System.Math.Sign(heading) * System.Math.PI); clip = "Walkbackwards"; rate = (float)sp / 2.6f; }
            else { h = Mathf.Clamp((float)heading, -MaxHip, MaxHip); clip = sp > 3.4 ? "Run" : "Walk"; rate = sp > 3.4 ? (float)sp / 7f : (float)sp / 2.5f; }
            low.Play(clip, speed: rate);
            if (a.Routed) up.Play("Run", speed: rate);
            else hipTarget = h;
        }

        /// <summary>坐骑：骑着 → 跟着走；跑散 → 自己跑慢慢停；死了 → 倒在原地（原型 updateMount）。</summary>
        void UpdateMount(Agent a, bool onHorse, float dt)
        {
            if (!mountRoot) return;
            var ml = rig.mount.Lower;
            bool still = false;
            if (onHorse)
            {
                deadMountPlaced = false;
                mountRoot.gameObject.SetActive(true);
                Place(mountRoot, null, a.X, a.Z, a.Yaw);
                double sp = a.Mount!.Speed;
                if (a.Mount.Stun > 0) ml.Play("CombatCritical");
                else if (sp < -0.3) ml.Play("Walkbackwards", speed: (float)System.Math.Abs(sp) / 2.5f);
                else if (sp < 0.4) ml.Play("Stand");
                else if (sp < 6) ml.Play("Walk", speed: (float)sp / 3.2f);
                else ml.Play("Run", speed: (float)(sp / (Defs.MountTopSpeed(a.Mount.Def, a.Troop.Skills.Riding) * 0.95)));
            }
            else if (a.LooseMount != null)
            {
                var h = a.LooseMount;
                deadMountPlaced = false;
                mountRoot.gameObject.SetActive(true);
                Place(mountRoot, null, h.X, h.Z, h.Yaw);
                if (h.Speed < 0.4) ml.Play("Stand");
                else if (h.Speed < 6) ml.Play("Walk", speed: (float)h.Speed / 3.2f);
                else ml.Play("Run", speed: (float)h.Speed / 14f);
            }
            else if (a.DeadMount != null)
            {
                if (!deadMountPlaced)
                {
                    deadMountPlaced = true;
                    deadMountShown = 0;
                    mountRoot.gameObject.SetActive(true);
                    Place(mountRoot, null, a.DeadMount.X, a.DeadMount.Z, a.DeadMount.Yaw);
                    if (mountWasLive) ml.Play("Death", once: true, fade: 0.1f);
                    else ml.Play("Death", time: 99f, fade: 0f);
                }
                deadMountShown += dt;
                still = deadMountShown > 2.5f;
                if (still) { mountCorpseT += dt; if (Sink(mountRoot, mountCorpseT, dt)) mountRoot.gameObject.SetActive(false); }
            }
            else
            {
                // 被别人骑走了（他的显示负责画）
                mountRoot.gameObject.SetActive(false);
                still = true;
            }
            if (hover)
            {
                bool dead = !onHorse && a.LooseMount == null && a.DeadMount != null;
                if (dead && !hover.crashed && !mountWasLive) hover.Snap(); else hover.crashed = dead;
                double hs = onHorse ? a.Mount!.Speed : a.LooseMount != null ? a.LooseMount.Speed : 0;
                hover.drive = Mathf.Clamp01((float)hs / 12f);
            }
            mountWasLive = onHorse || a.LooseMount != null;
            if (!still) rig.mount.Tick(dt);
        }

        public void Destroy()
        {
            if (Root) Object.Destroy(Root);
            if (mountRoot) Object.Destroy(mountRoot.gameObject);
        }
    }
}
