"""Run in Blender to rebuild the shared low-poly fuel pickup and its Unity export."""
from pathlib import Path
import math
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
parts = []

def box(name, location, size, bevel=0.015, angle=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.rotation_euler[1] = angle
    if bevel:
        modifier = obj.modifiers.new('Small chamfer', 'BEVEL')
        modifier.width = bevel
        modifier.segments = 1
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    parts.append(obj)
    return obj

# Blender Z is up. All parts share one material so every fuel uses its exact matte color.
box('Tank', (0, 0, .365), (.72, .36, .73), .045)
box('Shoulder', (0, 0, .725), (.65, .32, .13), .035)
box('Base seam', (0, 0, .04), (.73, .37, .07), .015)
# Open carrying handle: two uprights and a bridge, leaving a real hole underneath.
box('Handle left', (-.025, 0, .845), (.065, .10, .20), .018)
box('Handle right', (.255, 0, .845), (.065, .10, .20), .018)
box('Handle bridge', (.115, 0, .945), (.345, .10, .07), .022)
# Raised filler neck and a faceted cap to one side of the handle.
for name, radius, depth, z in [('Filler neck', .077, .06, .795), ('Cap', .098, .035, .835)]:
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=radius, depth=depth, location=(-.235, 0, z))
    obj = bpy.context.object
    obj.name = name
    parts.append(obj)
box('Cap grip', (-.235, 0, .86), (.13, .035, .025), .006)
# The characteristic pressed X, shallow and visible on both broad faces.
for side in (-1, 1):
    for angle in (-math.radians(36), math.radians(36)):
        box('Pressed reinforcement', (0, side * .183, .38), (.035, .025, .55), .009, angle)

bpy.ops.object.select_all(action='DESELECT')
for obj in parts:
    obj.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
can = bpy.context.object
can.name = 'FuelJerryCan'
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
# Center and normalize the mesh to a one-unit tall pickup. Existing trigger sizes stay unchanged.
low = Vector(tuple(min(v.co[i] for v in can.data.vertices) for i in range(3)))
high = Vector(tuple(max(v.co[i] for v in can.data.vertices) for i in range(3)))
center = (low + high) * .5
height = high.z - low.z
for vertex in can.data.vertices:
    vertex.co = (vertex.co - center) / height
can.location = (0, 0, 0)
tri = can.modifiers.new('Export triangles', 'TRIANGULATE')
bpy.ops.object.modifier_apply(modifier=tri.name)
can.data.name = 'FuelJerryCanMesh'
material = bpy.data.materials.new('Matte fuel preview')
material.diffuse_color = (95/255, 58/255, 41/255, 1)
material.use_nodes = True
shader = material.node_tree.nodes.get('Principled BSDF')
shader.inputs['Base Color'].default_value = material.diffuse_color
shader.inputs['Metallic'].default_value = 0
shader.inputs['Roughness'].default_value = 1
shader.inputs['Specular IOR Level'].default_value = 0
can.data.materials.clear()
can.data.materials.append(material)
assert all(len(p.vertices) == 3 for p in can.data.polygons)
assert len(can.data.polygons) < 1000
bpy.ops.export_scene.fbx(filepath=str(ROOT/'Assets/Resources/FuelJerryCan.fbx'), use_selection=True,
    object_types={'MESH'}, axis_forward='-Z', axis_up='Y', global_scale=1,
    apply_unit_scale=True, bake_space_transform=True, add_leaf_bones=False, bake_anim=False,
    mesh_smooth_type='FACE', path_mode='AUTO')

# A simple camera and lights also make the editable Blender source easy to preview.
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 24
scene.render.resolution_x = 640
scene.render.resolution_y = 640
scene.render.resolution_percentage = 100
scene.world.color = (.7, .7, .7)
bpy.ops.object.camera_add(location=(1.5, -2.5, 1.35))
camera = bpy.context.object
camera.rotation_euler = (-camera.location).to_track_quat('-Z', 'Y').to_euler()
camera.data.type = 'ORTHO'
camera.data.ortho_scale = 1.4
scene.camera = camera
for location, energy, size in [((2, -3, 4), 200, 4), ((-2, -1, 2), 100, 3), ((0, 3, 3), 160, 3)]:
    bpy.ops.object.light_add(type='AREA', location=location)
    light = bpy.context.object
    light.data.energy = energy
    light.data.shape = 'DISK'
    light.data.size = size
    light.rotation_euler = (-light.location).to_track_quat('-Z', 'Y').to_euler()
bpy.ops.object.select_all(action='DESELECT')
can.select_set(True)
bpy.context.view_layer.objects.active = can
scene.render.image_settings.file_format = 'PNG'
scene.render.filepath = '/tmp/FuelJerryCan-preview.png'
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'Blender/FuelJerryCan.blend'))
print(f'FuelJerryCan: {len(can.data.vertices)} vertices, {len(can.data.polygons)} triangles')
bpy.ops.render.render(write_still=True)
