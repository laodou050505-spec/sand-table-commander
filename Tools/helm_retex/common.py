import numpy as np, cv2
def lin(v): return np.where(v<=0.04045, v/12.92, np.power((np.abs(v)+0.055)/1.055, 2.4))
def rd(n, decode=True):
    x = cv2.imread(f'/tmp/helm/{n}.exr', cv2.IMREAD_UNCHANGED)[..., [2,1,0]].astype(np.float32)
    return lin(x) if decode else x
