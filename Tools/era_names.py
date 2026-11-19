"""各风格的兵种名——图片这边的唯一出处（11-13 用户：“所有不同风格的名字都需要调整”：换了风格，旗牌上的名字换了，
但“目标·弓兵”状态牌和“弓兵 · 冲锋”下令反馈牌还是中古的叫法）。
用它的脚本：make_field_ui.py（旗牌）、make_state_badges.py（目标牌）、make_order_badges.py（下令反馈牌）。
改名：改这里 + Assets/_WarTable/Runtime/Loc.cs 里对应的 group.<兵种>@<后缀>（文字那边），然后三个脚本各跑一遍、GameSetup.Setup()。
键 = Settings.EraSuffix（'' 剑盾 / 原中古、g 铁誓 / 哥特、m 硝烟 / 现代、f 星尘 / 未来、s 深空）。"""
ZH = {
    '': {'inf': '步兵', 'arch': '弓兵', 'cav': '骑兵', 'ha': '骑射'},
    'g': {'inf': '铁卫', 'arch': '射手', 'cav': '铁骑', 'ha': '猎骑'},
    'f': {'inf': '锋卫', 'arch': '光弓', 'cav': '机骑', 'ha': '游骑'},
    'm': {'inf': '突击', 'arch': '枪手', 'cav': '摩骑', 'ha': '飞车'},
    's': {'inf': '重甲', 'arch': '轨枪', 'cav': '星骑', 'ha': '掠车'},
}
# 旗牌上的英文（短，全大写）
EN = {
    '': {'inf': 'INF', 'arch': 'BOW', 'cav': 'CAV', 'ha': 'H.ARCH'},
    'g': {'inf': 'GUARD', 'arch': 'RIFLE', 'cav': 'LANCE', 'ha': 'RIDER'},
    'f': {'inf': 'VANGUARD', 'arch': 'BOWS', 'cav': 'MECH', 'ha': 'RUNNER'},
    'm': {'inf': 'ASSAULT', 'arch': 'RIFLES', 'cav': 'BIKERS', 'ha': 'RAIDERS'},
    's': {'inf': 'HEAVIES', 'arch': 'RAILGUNS', 'cav': 'STRIDERS', 'ha': 'CRAWLERS'},
}
# 状态牌 / 下令反馈牌上的英文（中古用全称，其余和旗牌一致）
EN_LONG = dict(EN); EN_LONG[''] = {'inf': 'INFANTRY', 'arch': 'ARCHERS', 'cav': 'CAVALRY', 'ha': 'HORSE ARCHERS'}
SUFFIXES = ['', 'g', 'm', 'f', 's']      # 顺序 = Settings.Era 的下标
def tag(sfx): return ('_' + sfx) if sfx else ''
