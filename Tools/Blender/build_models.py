"""Genera los modelos low-poly de Lucky Little Civilization con bmesh y los exporta a FBX para Unity.

Uso (desde la raíz del repo, Blender 4.2+ / 5.x):

  blender --background --python Tools/Blender/build_models.py -- --out Assets/Models --blend Tools/Blender/LuckyModels.blend

Convenciones (verificadas con una malla de prueba importada en Unity 6):
  - 1 unidad = 1 m; pivote en el suelo (z = 0) salvo el marcador de alerta, centrado en el origen.
  - El frente de un personaje es -Y en Blender, que en Unity es +Z (hacia donde mira VillagerActor).
  - El lado de los edificios que mira a la cámara es +Y en Blender, que en Unity es -Z.
  - La paleta está en sRGB (lo que se ve en el inspector de Unity) y se convierte a lineal al crear cada material.
  - Un material por color; URP los importa como "Universal Render Pipeline/Lit" con ese color base.
"""

import argparse
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

# --------------------------------------------------------------------------- paleta (sRGB)

PALETTE = {
    "WoodDark": (0.40, 0.26, 0.15),
    "WoodLight": (0.72, 0.52, 0.32),
    "Stone": (0.60, 0.60, 0.62),
    "StoneDark": (0.45, 0.45, 0.48),
    "Rock": (0.55, 0.52, 0.50),
    "Thatch": (0.84, 0.70, 0.38),
    "RoofRed": (0.70, 0.28, 0.22),
    "Plaster": (0.92, 0.86, 0.74),
    "Soil": (0.46, 0.31, 0.18),
    "SoilDark": (0.36, 0.23, 0.13),
    "Crop": (0.35, 0.68, 0.28),
    "CropLight": (0.56, 0.78, 0.30),
    "Leaf": (0.27, 0.54, 0.25),
    "LeafDark": (0.18, 0.42, 0.20),
    "LeafLight": (0.42, 0.68, 0.30),
    "Grass": (0.44, 0.62, 0.31),
    "GrassDark": (0.38, 0.56, 0.27),
    "Water": (0.28, 0.58, 0.92),
    "Skin": (0.95, 0.78, 0.66),
    "Hair": (0.32, 0.21, 0.12),
    "Eye": (0.12, 0.10, 0.10),
    "Pants": (0.38, 0.36, 0.44),
    "ClothNormal": (0.80, 0.75, 0.65),
    "ClothStrong": (0.85, 0.30, 0.25),
    "ClothSmart": (0.30, 0.50, 0.90),
    "ClothFast": (0.90, 0.80, 0.25),
    "HatSmart": (0.20, 0.30, 0.62),
    "BandFast": (0.85, 0.45, 0.15),
    "Metal": (0.70, 0.72, 0.78),
    "Rope": (0.76, 0.66, 0.46),
    "Banner": (0.90, 0.25, 0.20),
    "Cobble": (0.80, 0.72, 0.56),
    "CobbleDark": (0.70, 0.62, 0.47),
    "Fire": (0.96, 0.50, 0.14),
    "FireLight": (1.00, 0.86, 0.30),
    "Alert": (0.95, 0.20, 0.15),
}

_materials = {}


def material(name):
    """Material Principled con color base lineal; se crea una sola vez por nombre."""
    if name in _materials:
        return _materials[name]
    srgb = PALETTE[name]
    linear = tuple(c ** 2.2 for c in srgb)
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*linear, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.9
    mat.diffuse_color = (*linear, 1.0)
    _materials[name] = mat
    return mat


# --------------------------------------------------------------------------- constructor

def _euler(rot):
    return Euler(tuple(math.radians(a) for a in rot), "XYZ").to_matrix().to_4x4()


def rotz(point, degrees):
    """Gira un punto (x, y, z) alrededor del eje Z."""
    a = math.radians(degrees)
    x, y, z = point
    return (x * math.cos(a) - y * math.sin(a), x * math.sin(a) + y * math.cos(a), z)


class Model:
    """Acumula primitivas en un solo bmesh; cada cara lleva el índice de su material."""

    def __init__(self, name, seed=1):
        self.name = name
        self.bm = bmesh.new()
        self.mat_names = []
        self.rng = random.Random(seed)

    def mat(self, name):
        if name not in self.mat_names:
            self.mat_names.append(name)
        return self.mat_names.index(name)

    @staticmethod
    def _faces_of(verts):
        """Caras de una primitiva recién creada: las enlazadas a sus vértices (el orden de bm.faces no es fiable)."""
        faces = set()
        for v in verts:
            if v.is_valid:
                faces.update(v.link_faces)
        return faces

    def _tag(self, faces, name):
        idx = self.mat(name)
        for f in faces:
            f.material_index = idx

    def box(self, size, center, mat, rot=(0, 0, 0)):
        m = Matrix.Translation(center) @ _euler(rot) @ Matrix.Diagonal((*size, 1.0))
        ret = bmesh.ops.create_cube(self.bm, size=1.0, matrix=m)
        self._tag(self._faces_of(ret["verts"]), mat)

    def cone(self, r1, r2, depth, center, mat, segments=8, rot=(0, 0, 0), scale=(1, 1, 1)):
        m = Matrix.Translation(center) @ _euler(rot) @ Matrix.Diagonal((*scale, 1.0))
        ret = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False, segments=segments,
                                    radius1=r1, radius2=r2, depth=depth, matrix=m)
        if r1 == 0 or r2 == 0:
            bmesh.ops.remove_doubles(self.bm, verts=ret["verts"], dist=1e-4)
        self._tag(self._faces_of(ret["verts"]), mat)

    def cyl(self, r, depth, center, mat, segments=8, rot=(0, 0, 0)):
        self.cone(r, r, depth, center, mat, segments, rot)

    def pyramid(self, half_width, height, base_center, mat):
        """Pirámide de base cuadrada alineada con los ejes."""
        x, y, z = base_center
        self.cone(half_width * math.sqrt(2), 0, height, (x, y, z + height / 2), mat, segments=4, rot=(0, 0, 45))

    def sphere(self, r, center, mat, subdiv=1, jitter=0.0, scale=(1, 1, 1)):
        m = Matrix.Translation(center) @ Matrix.Diagonal((*scale, 1.0))
        ret = bmesh.ops.create_icosphere(self.bm, subdivisions=subdiv, radius=r, matrix=m)
        if jitter:
            for v in ret["verts"]:
                v.co += Vector(tuple(self.rng.uniform(-jitter, jitter) for _ in range(3)))
        self._tag(self._faces_of(ret["verts"]), mat)

    def prism(self, width_x, depth_y, height, base_center, mat):
        """Tejado a dos aguas: triángulo en el plano YZ extruido a lo largo de X."""
        bm = self.bm
        x, y, z = base_center
        hx, hy = width_x / 2, depth_y / 2
        v = [bm.verts.new(c) for c in (
            (x - hx, y - hy, z), (x - hx, y + hy, z), (x - hx, y, z + height),
            (x + hx, y - hy, z), (x + hx, y + hy, z), (x + hx, y, z + height))]
        faces = [bm.faces.new([v[i] for i in idx])
                 for idx in ((0, 2, 1), (3, 4, 5), (0, 3, 5, 2), (1, 2, 5, 4), (0, 1, 4, 3))]
        self._tag(faces, mat)

    def grid(self, size, segments, mat_a, mat_b, ratio_b, z_jitter):
        ret = bmesh.ops.create_grid(self.bm, x_segments=segments, y_segments=segments, size=1.0)
        extent = max(abs(v.co.x) for v in ret["verts"])
        k = (size / 2) / extent
        lo, hi = z_jitter
        for v in ret["verts"]:
            v.co = Vector((v.co.x * k, v.co.y * k, self.rng.uniform(lo, hi)))
        ia, ib = self.mat(mat_a), self.mat(mat_b)
        for f in sorted(self._faces_of(ret["verts"]), key=lambda f: tuple(f.calc_center_median())):
            f.material_index = ib if self.rng.random() < ratio_b else ia

    def finish(self, recalc=True):
        bm = self.bm
        bm.faces.ensure_lookup_table()
        if recalc:
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        mesh = bpy.data.meshes.new(self.name)
        bm.to_mesh(mesh)
        bm.free()
        for n in self.mat_names:
            mesh.materials.append(material(n))
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        return obj


# --------------------------------------------------------------------------- modelos

def villager(cls):
    cloth = "Cloth" + cls
    md = Model("Villager" + cls, seed=hash(cls) & 0xFFFF)
    # piernas y botas
    for x in (-0.09, 0.09):
        md.box((0.14, 0.16, 0.30), (x, 0.0, 0.15), "Pants")
        md.box((0.16, 0.20, 0.08), (x, -0.01, 0.04), "WoodDark")
    # torso troncocónico, más ancho que profundo
    md.cone(0.32, 0.25, 0.42, (0.0, 0.0, 0.51), cloth, segments=4, rot=(0, 0, 45), scale=(1.0, 0.75, 1.0))
    md.box((0.44, 0.33, 0.06), (0.0, 0.0, 0.34), "Hair")  # cinturón
    # brazos y manos
    for side in (-1, 1):
        md.box((0.09, 0.09, 0.34), (side * 0.28, 0.0, 0.55), cloth, rot=(0, side * 8, 0))
        md.box((0.10, 0.10, 0.10), (side * 0.31, 0.0, 0.36), "Skin")
    # cabeza, pelo y cara (frente = -Y)
    md.box((0.34, 0.30, 0.30), (0.0, 0.0, 0.87), "Skin")
    md.box((0.36, 0.08, 0.20), (0.0, 0.14, 0.95), "Hair")
    md.box((0.36, 0.04, 0.06), (0.0, -0.15, 0.99), "Hair")
    for x in (-0.07, 0.07):
        md.box((0.05, 0.02, 0.06), (x, -0.155, 0.89), "Eye")
    md.box((0.04, 0.04, 0.04), (0.0, -0.165, 0.83), "Skin")

    if cls == "Normal":
        md.box((0.36, 0.32, 0.08), (0.0, 0.0, 1.05), "Hair")
        md.cyl(0.30, 0.04, (0.0, 0.0, 1.08), "Thatch", segments=10)
        md.cyl(0.18, 0.12, (0.0, 0.0, 1.15), "Thatch", segments=10)
    elif cls == "Strong":
        md.sphere(0.24, (0.0, 0.0, 1.0), "Metal", subdiv=1, scale=(1.0, 0.95, 0.75))
        md.box((0.06, 0.04, 0.16), (0.0, -0.17, 0.93), "Metal")  # nasal
        for side in (-1, 1):
            md.box((0.16, 0.16, 0.08), (side * 0.28, 0.0, 0.74), "Metal")  # hombreras
            md.box((0.03, 0.03, 0.14), (side * 0.10, 0.0, 1.18), "Metal")  # cuernos cortos
    elif cls == "Smart":
        md.box((0.36, 0.32, 0.08), (0.0, 0.0, 1.05), "Hair")
        md.cyl(0.30, 0.04, (0.0, 0.0, 1.10), "HatSmart", segments=10)
        md.cone(0.22, 0.0, 0.55, (0.02, 0.02, 1.38), "HatSmart", segments=8)
        md.box((0.14, 0.10, 0.18), (0.34, -0.08, 0.38), "RoofRed")  # libro
    elif cls == "Fast":
        md.box((0.36, 0.32, 0.08), (0.0, 0.0, 1.05), "Hair")
        md.box((0.37, 0.33, 0.05), (0.0, 0.0, 0.97), "BandFast")
        md.box((0.08, 0.26, 0.05), (0.06, 0.26, 0.98), "BandFast", rot=(0, 0, 10))  # cola de la cinta
        md.box((0.40, 0.30, 0.06), (0.0, 0.0, 0.70), "BandFast")  # pañuelo al cuello
    return md.finish()


def house():
    md = Model("House", seed=11)
    md.box((1.60, 1.50, 0.14), (0.0, 0.0, 0.07), "StoneDark")
    md.box((1.45, 1.35, 1.05), (0.0, 0.0, 0.665), "Plaster")
    for x in (-0.70, 0.70):
        for y in (-0.65, 0.65):
            md.box((0.10, 0.10, 1.05), (x, y, 0.665), "WoodDark")
    for y in (-0.69, 0.69):
        md.box((1.50, 0.10, 0.08), (0.0, y, 1.15), "WoodDark")
    md.prism(1.95, 1.80, 0.85, (0.0, 0.0, 1.12), "RoofRed")
    md.box((2.00, 0.14, 0.10), (0.0, 0.0, 1.95), "WoodDark")
    # frente (+Y): puerta, escalón y ventana
    md.box((0.46, 0.06, 0.78), (0.0, 0.69, 0.53), "WoodDark")
    md.box((0.04, 0.04, 0.04), (0.15, 0.73, 0.55), "Metal")
    md.box((0.60, 0.30, 0.08), (0.0, 0.85, 0.04), "Stone")
    md.box((0.36, 0.05, 0.36), (-0.45, 0.685, 0.75), "WoodLight")
    md.box((0.28, 0.06, 0.28), (-0.45, 0.69, 0.75), "Water")
    # ventana lateral (+X)
    md.box((0.05, 0.36, 0.36), (0.735, 0.0, 0.75), "WoodLight")
    md.box((0.06, 0.28, 0.28), (0.74, 0.0, 0.75), "Water")
    # chimenea sobre la vertiente trasera
    md.box((0.26, 0.26, 0.80), (0.50, -0.35, 1.62), "StoneDark")
    md.box((0.32, 0.32, 0.06), (0.50, -0.35, 2.03), "Stone")
    return md.finish()


def farm():
    md = Model("Farm", seed=21)
    md.box((5.0, 4.0, 0.22), (0.0, 0.0, 0.11), "Soil")
    for y in (-2.0, 2.0):
        md.box((5.1, 0.12, 0.16), (0.0, y, 0.20), "WoodDark")
    for x in (-2.5, 2.5):
        md.box((0.12, 4.1, 0.16), (x, 0.0, 0.20), "WoodDark")
    for i, y in enumerate((-1.5, -0.5, 0.5, 1.5)):
        md.box((4.4, 0.55, 0.14), (0.0, y, 0.27), "SoilDark")
        for j in range(7):
            x = -1.8 + j * 0.6 + md.rng.uniform(-0.05, 0.05)
            s = md.rng.uniform(0.75, 1.15)
            mat = "Crop" if (i + j) % 2 == 0 else "CropLight"
            md.cone(0.17, 0.0, 0.42, (x, y + md.rng.uniform(-0.05, 0.05), 0.34 + 0.21 * s), mat,
                    segments=5, rot=(0, 0, md.rng.uniform(0, 72)), scale=(s, s, s))
    # valla parcial en el frente (+Y) y el lado -X
    for x in (-2.5, -0.85, 0.85, 2.5):
        md.box((0.14, 0.14, 0.80), (x, 2.05, 0.50), "WoodLight")
    for z in (0.45, 0.72):
        md.box((5.0, 0.06, 0.08), (0.0, 2.05, z), "WoodLight")
    for y in (-2.0, -0.7, 0.7):
        md.box((0.14, 0.14, 0.80), (-2.55, y, 0.50), "WoodLight")
    for z in (0.45, 0.72):
        md.box((0.06, 4.0, 0.08), (-2.55, 0.0, z), "WoodLight")
    # espantapájaros
    sx, sy = 2.1, 1.45
    md.box((0.10, 0.10, 1.60), (sx, sy, 0.80), "WoodDark")
    md.box((1.00, 0.08, 0.08), (sx, sy, 1.25), "WoodDark")
    md.box((0.40, 0.22, 0.50), (sx, sy, 1.10), "ClothNormal")
    md.sphere(0.16, (sx, sy, 1.56), "Thatch", subdiv=1)
    md.cone(0.30, 0.0, 0.26, (sx, sy, 1.78), "Thatch", segments=8)
    # barril de agua
    bx, by = -2.1, 1.55
    md.cyl(0.28, 0.60, (bx, by, 0.30), "WoodDark", segments=10)
    for z in (0.15, 0.45):
        md.cyl(0.30, 0.05, (bx, by, z), "Metal", segments=10)
    md.cyl(0.24, 0.03, (bx, by, 0.60), "Water", segments=10)
    return md.finish()


def well():
    md = Model("Well", seed=31)
    md.cone(0.95, 0.88, 0.90, (0.0, 0.0, 0.45), "Stone", segments=8)
    md.cyl(1.00, 0.14, (0.0, 0.0, 0.95), "StoneDark", segments=8)
    md.cyl(0.72, 0.04, (0.0, 0.0, 0.92), "Water", segments=8)
    for i in range(6):
        a = i * 60 + 15
        md.box((0.30, 0.20, 0.22), rotz((0.0, 0.92, 0.30 + (i % 2) * 0.25), a), "StoneDark", rot=(0, 0, a))
    for x in (-0.85, 0.85):
        md.box((0.16, 0.16, 1.88), (x, 0.0, 1.96), "WoodDark")
    md.box((2.00, 0.12, 0.12), (0.0, 0.0, 2.85), "WoodDark")
    md.cyl(0.10, 1.60, (0.0, 0.0, 2.45), "WoodLight", segments=8, rot=(0, 90, 0))
    md.box((0.08, 0.08, 0.30), (0.95, 0.0, 2.30), "WoodDark")
    md.box((0.08, 0.26, 0.08), (0.95, -0.12, 2.15), "WoodDark")
    md.cyl(0.02, 0.90, (0.0, 0.0, 2.00), "Rope", segments=5)
    md.cone(0.14, 0.19, 0.28, (0.0, 0.0, 1.50), "WoodDark", segments=8)
    md.cyl(0.20, 0.04, (0.0, 0.0, 1.60), "Metal", segments=8)
    md.prism(2.40, 1.50, 0.65, (0.0, 0.0, 2.85), "Thatch")
    md.box((2.50, 0.12, 0.10), (0.0, 0.0, 3.48), "WoodDark")
    return md.finish()


def plaza():
    md = Model("Plaza", seed=41)
    md.cyl(2.40, 0.08, (0.0, 0.0, 0.04), "Cobble", segments=16)
    for _ in range(12):
        a, r = md.rng.uniform(0, 360), md.rng.uniform(0.9, 2.1)
        md.cyl(md.rng.uniform(0.18, 0.32), 0.02, rotz((r, 0.0, 0.085), a), "CobbleDark", segments=7)
    # hoguera central
    for i in range(8):
        a = i * 45
        md.box((0.28, 0.20, 0.22), rotz((0.50, 0.0, 0.19), a), "Rock", rot=(0, 0, a + md.rng.uniform(-10, 10)))
    md.cyl(0.07, 0.70, (0.0, 0.0, 0.16), "WoodDark", segments=6, rot=(0, 90, 0))
    md.cyl(0.07, 0.70, (0.0, 0.0, 0.20), "WoodDark", segments=6, rot=(90, 0, 0))
    md.cone(0.22, 0.0, 0.50, (0.0, 0.0, 0.47), "Fire", segments=5)
    md.cone(0.12, 0.0, 0.65, (0.03, -0.02, 0.56), "FireLight", segments=5, rot=(0, 0, 36))
    # estandarte (material "Banner" se tiñe con la felicidad)
    px, py = 1.6, -1.2
    md.box((0.12, 0.12, 2.80), (px, py, 1.40), "WoodDark")
    md.box((0.80, 0.08, 0.08), (px, py, 2.70), "WoodDark")
    md.sphere(0.10, (px, py, 2.86), "Metal", subdiv=1)
    md.box((0.70, 0.05, 0.90), (px, py, 2.20), "Banner")
    # bancos alrededor de la hoguera
    for a in (200, 330):
        cx, cy, _ = rotz((1.35, 0.0, 0.0), a)
        md.box((1.10, 0.35, 0.08), (cx, cy, 0.45), "WoodLight", rot=(0, 0, a + 90))
        for off in (-0.42, 0.42):
            lx, ly, _ = rotz((1.35, off, 0.0), a)
            md.box((0.10, 0.30, 0.40), (lx, ly, 0.22), "WoodDark", rot=(0, 0, a + 90))
    md.box((0.40, 0.40, 0.40), (-1.75, -1.05, 0.28), "WoodLight", rot=(0, 0, 20))
    return md.finish()


def tower():
    md = Model("Tower", seed=51)
    for x in (-0.55, 0.55):
        for y in (-0.55, 0.55):
            md.box((0.16, 0.16, 2.90), (x, y, 1.45), "WoodDark")
    for s in (-1, 1):
        for sign in (-1, 1):
            md.box((1.50, 0.07, 0.07), (0.0, s * 0.58, 1.40), "WoodLight", rot=(0, sign * 42, 0))
            md.box((0.07, 1.50, 0.07), (s * 0.58, 0.0, 1.40), "WoodLight", rot=(sign * 42, 0, 0))
    md.box((1.90, 1.90, 0.16), (0.0, 0.0, 2.95), "WoodLight")
    for x in (-0.90, 0.0, 0.90):
        for y in (-0.90, 0.0, 0.90):
            if x == 0.0 and y == 0.0:
                continue
            md.box((0.08, 0.08, 0.90), (x, y, 3.45), "WoodDark")
    for z in (3.35, 3.78):
        for s in (-0.90, 0.90):
            md.box((1.86, 0.06, 0.06), (0.0, s, z), "WoodDark")
            md.box((0.06, 1.86, 0.06), (s, 0.0, z), "WoodDark")
    for x in (-0.72, 0.72):
        for y in (-0.72, 0.72):
            md.box((0.10, 0.10, 1.27), (x, y, 3.67), "WoodDark")
    for s in (-1.30, 1.30):
        md.box((2.72, 0.12, 0.12), (0.0, s, 4.30), "WoodDark")
        md.box((0.12, 2.72, 0.12), (s, 0.0, 4.30), "WoodDark")
    md.pyramid(1.30, 0.90, (0.0, 0.0, 4.30), "RoofRed")
    md.box((0.05, 0.05, 0.70), (0.0, 0.0, 5.50), "WoodDark")
    md.box((0.50, 0.04, 0.30), (0.27, 0.0, 5.66), "Banner")
    # escalera en el frente (+Y) y brasero
    for x in (-0.25, 0.25):
        md.box((0.06, 0.06, 2.90), (x, 0.72, 1.45), "WoodLight")
    for i in range(7):
        md.box((0.60, 0.06, 0.06), (0.0, 0.72, 0.35 + i * 0.40), "WoodLight")
    md.cyl(0.20, 0.30, (0.45, -0.45, 3.18), "Metal", segments=8)
    md.cone(0.14, 0.0, 0.30, (0.45, -0.45, 3.48), "Fire", segments=5)
    return md.finish()


def scaffold():
    md = Model("Scaffold", seed=61)
    md.box((1.60, 1.60, 0.30), (0.0, 0.0, 0.15), "StoneDark")
    md.box((1.45, 0.14, 0.70), (0.0, -0.65, 0.65), "Plaster")
    md.box((0.50, 0.14, 0.30), (-0.45, -0.65, 1.15), "Plaster")
    md.box((0.14, 1.30, 0.50), (-0.65, 0.0, 0.55), "Plaster")
    for x in (-0.85, 0.85):
        for y in (-0.85, 0.85):
            md.box((0.10, 0.10, 2.00), (x, y, 1.00), "WoodLight")
    for z in (1.10, 1.90):
        for s in (-0.85, 0.85):
            md.box((1.80, 0.08, 0.08), (0.0, s, z), "WoodLight")
            md.box((0.08, 1.80, 0.08), (s, 0.0, z), "WoodLight")
    for y in (0.55, 0.85):
        md.box((1.90, 0.26, 0.05), (0.0, y, 1.16), "WoodDark")
    for x in (0.15, 0.55):
        md.box((0.05, 0.05, 1.30), (x, 1.00, 0.65), "WoodDark")
    for i in range(4):
        md.box((0.46, 0.05, 0.05), (0.35, 1.00, 0.30 + i * 0.28), "WoodDark")
    for i, (dx, dz) in enumerate(((0.0, 0.36), (0.0, 0.48), (0.32, 0.36))):
        md.box((0.30, 0.15, 0.12), (0.45 + dx, -0.40, dz), "RoofRed", rot=(0, 0, i * 8))
    md.cone(0.10, 0.13, 0.22, (-0.40, 0.50, 0.41), "WoodDark", segments=8)
    return md.finish()


def tree_round():
    md = Model("TreeRound", seed=71)
    md.cone(0.18, 0.13, 1.10, (0.0, 0.0, 0.55), "WoodDark", segments=6)
    md.sphere(0.95, (0.0, 0.0, 1.75), "Leaf", subdiv=1, jitter=0.08)
    md.sphere(0.70, (0.50, 0.25, 2.05), "LeafLight", subdiv=1, jitter=0.06)
    md.sphere(0.65, (-0.45, -0.30, 2.10), "LeafDark", subdiv=1, jitter=0.06)
    md.sphere(0.50, (0.10, -0.50, 2.45), "Leaf", subdiv=1, jitter=0.05)
    return md.finish()


def tree_pine():
    md = Model("TreePine", seed=81)
    md.cone(0.15, 0.11, 0.90, (0.0, 0.0, 0.45), "WoodDark", segments=6)
    for r, h, base, mat, yaw in ((1.05, 1.20, 0.55, "LeafDark", 0), (0.85, 1.10, 1.30, "Leaf", 30),
                                 (0.60, 1.00, 2.05, "LeafDark", 0), (0.35, 0.80, 2.75, "LeafLight", 30)):
        md.cone(r, 0.0, h, (0.0, 0.0, base + h / 2), mat, segments=6, rot=(0, 0, yaw))
    return md.finish()


def bush():
    md = Model("Bush", seed=91)
    md.sphere(0.42, (0.0, 0.0, 0.34), "Leaf", subdiv=1, jitter=0.04)
    md.sphere(0.30, (0.35, 0.10, 0.28), "LeafLight", subdiv=1, jitter=0.03)
    md.sphere(0.28, (-0.30, -0.15, 0.30), "LeafDark", subdiv=1, jitter=0.03)
    return md.finish()


def rock():
    md = Model("Rock", seed=101)
    md.sphere(0.50, (0.0, 0.0, 0.24), "Rock", subdiv=1, jitter=0.08, scale=(1.2, 0.9, 0.6))
    md.sphere(0.25, (0.55, 0.20, 0.14), "StoneDark", subdiv=1, jitter=0.04, scale=(1.0, 1.0, 0.7))
    return md.finish()


def ground():
    md = Model("Ground", seed=111)
    md.grid(60.0, 30, "Grass", "GrassDark", 0.3, (-0.05, 0.02))
    return md.finish(recalc=False)


def alert():
    md = Model("Alert", seed=121)
    md.box((0.22, 0.22, 0.60), (0.0, 0.0, 0.25), "Alert")
    md.box((0.22, 0.22, 0.20), (0.0, 0.0, -0.30), "Alert")
    return md.finish()


BUILDERS = [
    lambda: villager("Normal"), lambda: villager("Strong"), lambda: villager("Smart"), lambda: villager("Fast"),
    house, farm, well, plaza, tower, scaffold, tree_round, tree_pine, bush, rock, ground, alert,
]


# --------------------------------------------------------------------------- exportación

def export_fbx(obj, path):
    for o in bpy.context.scene.objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True,
        axis_forward="-Z", axis_up="Y", mesh_smooth_type="OFF", use_mesh_modifiers=True,
        add_leaf_bones=False, path_mode="AUTO", embed_textures=False, use_custom_props=False)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", required=True, help="Carpeta de salida de los FBX (p. ej. Assets/Models)")
    parser.add_argument("--blend", default=None, help="Ruta opcional donde guardar un .blend con todos los modelos")
    args = parser.parse_args(argv)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    os.makedirs(args.out, exist_ok=True)

    objects = [build() for build in BUILDERS]
    for obj in objects:
        export_fbx(obj, os.path.join(args.out, obj.name + ".fbx"))
        print("EXPORTED", obj.name, len(obj.data.polygons), "caras")

    if args.blend:
        # Escaparate: todos los modelos en fila para revisarlos en Blender.
        x = 0.0
        for obj in objects:
            if obj.name == "Ground":
                obj.location = (20.0, -30.0, -0.1)
                continue
            obj.location = (x, 0.0, 0.0)
            x += max(obj.dimensions.x, 2.0) + 2.0
        os.makedirs(os.path.dirname(os.path.abspath(args.blend)), exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(args.blend))
        print("SAVED_BLEND", args.blend)


if __name__ == "__main__":
    main()
