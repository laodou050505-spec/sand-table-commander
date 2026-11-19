import numpy as np
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components
d=np.load('faces.npz'); pos=d['pos']; px=np.load('fcol.npy')
# 按顶点坐标合并（FBX 拆了 UV 缝）
v=pos.reshape(-1,3); key=np.round(v*1e4).astype(np.int64)
_,vid=np.unique(key,axis=0,return_inverse=True); vid=vid.reshape(-1,3)
n=len(vid); nv=vid.max()+1
r=np.repeat(np.arange(n),3); g=coo_matrix((np.ones(3*n),(r,vid.ravel())),shape=(n,nv)).tocsr()
adj=g@g.T
k,lab=connected_components(adj,directed=False)
np.save('flab.npy',lab)
P=pos.mean(1); L=px.mean(1)
cnt=np.bincount(lab); order=np.argsort(-cnt)
print('components',k)
for c in order[:14]:
    m=lab==c
    print(c,cnt[c],'min',P[m].min(0).round(2),'max',P[m].max(0).round(2),'L',L[m].mean().round(2),'rgb',px[m].mean(0).round(2))
