# 把一张 UV0 贴图贴到战盔上，渲染 4 个视角拼成一张（核对用）
import bpy, sys, mathutils
args = sys.argv[sys.argv.index('--')+1:]
tex, outp = args[0], args[1]
emit = len(args) > 2 and args[2] == 'emit'
bpy.ops.wm.open_mainfile(filepath='/tmp/helm/stage1.blend')
ob = bpy.data.objects['Helm']; me = ob.data
me.uv_layers.active = me.uv_layers['UV0']
for p in me.polygons: p.use_smooth = True
mat = bpy.data.materials.new('chk'); mat.use_nodes = True; nt = mat.node_tree
img = bpy.data.images.load(tex); img.colorspace_settings.name = 'sRGB'
t = nt.nodes.new('ShaderNodeTexImage'); t.image = img; t.interpolation = 'Closest'
uvn = nt.nodes.new('ShaderNodeUVMap'); uvn.uv_map = 'UV0'; nt.links.new(uvn.outputs[0], t.inputs[0])
bs = [n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'][0]
if emit:
    out = [n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL'][0]; em = nt.nodes.new('ShaderNodeEmission'); nt.links.new(t.outputs[0], em.inputs[0]); nt.links.new(em.outputs[0], out.inputs[0])
else:
    nt.links.new(t.outputs[0], bs.inputs['Base Color']); bs.inputs['Roughness'].default_value = 0.6
me.materials.clear(); me.materials.append(mat)
sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE'
sc.render.resolution_x = sc.render.resolution_y = 700
w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; [n for n in w.node_tree.nodes if n.type == 'BACKGROUND'][0].inputs[1].default_value = 2.0
sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3; sc.collection.objects.link(sun); sun.rotation_euler = (0.6, 0.3, 0.8)
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
files = []
for name, d in [('front', (1, 0, 0.15)), ('knight', (0.3, -1, 0.2)), ('orc', (0.3, 1, 0.2)), ('back', (-1, 0, 0.3)), ('skull', (0.6, 0.8, 0.0))]:
    cam.location = mathutils.Vector(d).normalized() * 2.4 + mathutils.Vector((0, -0.05, 0))
    cam.rotation_euler = (mathutils.Vector((0, -0.05, 0)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    f = f'/tmp/helm/_v_{name}.png'; sc.render.filepath = f; bpy.ops.render.render(write_still=True); files.append(f)
import subprocess
subprocess.run(['python3', '-c', f"from PIL import Image\nims=[Image.open(x).convert('RGB') for x in {files!r}]\no=Image.new('RGB',(700*len(ims),700))\n[o.paste(im,(i*700,0)) for i,im in enumerate(ims)]\no.save('{outp}')"])
print('RENDERED', outp)
