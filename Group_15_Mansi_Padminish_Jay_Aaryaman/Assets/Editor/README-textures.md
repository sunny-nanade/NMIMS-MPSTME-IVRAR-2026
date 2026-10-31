# Poly Haven textures — how to use them in this project

Everything here is automated by `Assets/Editor/PolyHavenImporter.cs`. You only need to
download the right files, drop them in the right folder, and set two numbers on the
material. This document explains those three steps.

Poly Haven assets are CC0 (public domain). No attribution is required, though it is
polite to credit them in the project write-up.

---

## 1. Which maps to download

Poly Haven gives you a long list of maps per texture. **Download exactly three:**

| Download this | Why |
| --- | --- |
| `*_diff_*`   | Base colour (albedo). The actual look of the surface. |
| `*_nor_gl_*` | Normal map, OpenGL convention. Surface detail without geometry. |
| `*_arm_*`    | Packed AO + Roughness + Metallic — three maps in one file's RGB channels. |

**Skip these:**

- `*_rough_*`, `*_ao_*`, `*_metal_*` — already inside `_arm`. Downloading them
  separately costs three texture samples instead of one, which matters at 72 FPS.
- `*_disp_*` — displacement. Nothing in this project reads it. We have no tessellation
  and the geometry is primitives. The importer logs a warning if one shows up.
- `*_nor_dx_*` — the DirectX-convention normal map. Its green channel is inverted
  relative to what Unity expects, so lighting reads inside-out: bumps become dents.
  Always take `_nor_gl`. The importer warns you if you grab `_nor_dx` by mistake.

### Why "ARM" exists

`arm` = **A**mbient occlusion, **R**oughness, **M**etallic, packed one per colour channel:

- **R** → ambient occlusion
- **G** → roughness
- **B** → metallic

One texture fetch instead of three. On mobile GPUs, texture bandwidth is usually the
thing you run out of first, so this is the single biggest cheap win available.

### File format

Poly Haven offers JPG and PNG for most maps.

- `_diff` and `_arm` → **JPG** is fine. Smaller download, and the compression artefacts
  are invisible once Unity recompresses to ASTC anyway.
- `_nor_gl` → **PNG**. JPEG artefacts in a normal map show up as visible blotchy
  shading across flat surfaces, because small colour errors become large angle errors.
  This one is worth the extra megabytes.

---

## 2. Which resolution to download

**Download 1k for everything.** The importer caps every texture at 1024 px anyway, so a
2k or 4k download is just disk space and import time you never get back.

The one exception worth making: a surface the user stands directly on and looks down at
from half a metre — the catwalk plank, the glass floor panel. There, detail is being
inspected at point-blank range, and 2k can be justified:

1. Download the 2k version.
2. Select the texture in Unity and set **Max Size** to 2048 by hand.
3. Leave everything else alone — your manual change is preserved (see §5).

Budget note: at 1024 px with ASTC 6x6, one map is roughly 0.2 MB on device. A full
three-map material is around 0.6 MB. That is cheap — you can afford a good number of
distinct materials. Raising a material to 2048 quadruples it to ~2.4 MB, which is why
it is worth doing deliberately rather than by default.

---

## 3. Where to put the files

```
Assets/Textures/Concrete/     concrete, plaster, asphalt, stone
Assets/Textures/Metal/        steel, rusted panels, grating, railings
Assets/Textures/Wood/         planks, plywood, beams
Assets/HDRIs/                 sky / environment lighting (see §6)
```

Anything dropped anywhere under `Assets/Textures/` is configured automatically. The
subfolder you choose is purely organisational — the importer only reads the filename.

Keep Poly Haven's original filenames. The importer identifies maps by the suffix tokens
in the name, so renaming `concrete_floor_02_arm_1k.jpg` to `my_concrete_arm.jpg` still
works (the `arm` token survives), but renaming it to `my_concrete_packed.jpg` does not.

---

## 4. What the importer sets for you

| Map | Texture Type | sRGB | Notes |
| --- | --- | --- | --- |
| `_diff` | Default | **on** | Colour data, so it needs gamma decoding. |
| `_nor_gl` | Normal Map | n/a | Unity handles the encoding itself. |
| `_arm` | Default | **off** | Values, not colour — see below. |
| `_rough`, `_ao`, `_metal` | Default | **off** | Same reason. |
| `_disp` | Default | **off** | Plus a warning that nothing uses it. |

And for every one of them:

- **Generate Mip Maps: on** — non-negotiable in VR. Without mips, any tiled surface
  viewed at an angle shimmers violently as you move your head, which is both ugly and a
  genuine comfort problem.
- **Max Size: 1024**
- **Android: ASTC 6x6**, **Standalone (Editor/PC): BC7**

### Why sRGB must be off for ARM

A roughness of 0.5 means "half rough" — it is a number, not a colour. If Unity treats it
as sRGB it applies a gamma curve on read, and 0.5 arrives at the shader as roughly 0.21.
Your surfaces come out far glossier than intended and no amount of tweaking the material
fixes it, because the data was corrupted at import. Same story for AO and metallic.

`_diff` is the opposite case: it genuinely is colour, authored in sRGB, so it genuinely
needs the decode. Hence sRGB on for that one and off for everything else.

---

## 5. Re-imports and manual overrides

The importer stamps each texture's **User Data** field with `polyhaven-pipeline-v1` once
it has configured it, and skips anything already stamped. Two consequences:

- Re-imports and Library rebuilds are fast — no redundant reconfiguration.
- **Any manual change you make afterwards sticks.** Bumping one texture to 2048, or
  switching a format, will not be reverted on the next import.

To force a texture back to pipeline defaults, clear its **User Data** field (bottom of
the Inspector, under Advanced) and reimport.

To force *everything* back to defaults, bump `PipelineVersion` in
`PolyHavenImporter.cs` and reimport the folder.

Files with no recognised Poly Haven suffix are **left completely untouched** and logged,
so hand-authored textures can live in these folders safely.

---

## 6. HDRIs are not auto-configured

`Assets/HDRIs/` is deliberately outside the importer's scope. Sky maps need the opposite
of everything above — cube shape, no 1024 cap, HDR-aware handling — so applying the
texture rules to them would be actively wrong.

Set one up by hand:

1. Download the `.exr` at **2k**. 4k is wasted on a Quest display, and the file is large.
2. Select it and set **Texture Shape: Cube**, **Mapping: Latitude-Longitude (Cylindrical)**.
3. Leave **Max Size** at 2048.
4. Create a material with the **Skybox/Panoramic** shader (or Skybox/Cubemap) and assign it.
5. Assign the material in **Window → Rendering → Lighting → Environment → Skybox Material**.

The project already has one in place as a working reference:
`Assets/Scenes/Acrophobia Exposure Therapy/Textures/lonely_road_afternoon_puresky_2k.exr`,
used by `M_Skybox_Afternoon.mat`. New HDRIs belong in `Assets/HDRIs/`.

---

## 7. Making a material

Use the **`Shader Graphs/PolyHavenLit`** shader (`Assets/Shaders/PolyHavenLit.shadergraph`).

1. Right-click in `Assets/Materials/` → **Create → Material**.
2. Set its shader to **Shader Graphs → PolyHavenLit**.
3. Assign **Base Map** ← `_diff`, **Normal Map** ← `_nor_gl`, **ARM Map** ← `_arm`.
4. Set **Tiling** (see §8).

### Why not just use URP/Lit?

URP's Lit shader expects **smoothness**, where 1.0 is a mirror. Poly Haven authors
**roughness**, where 1.0 is chalk. They are exact opposites. Plugging a roughness map
into a smoothness slot gives you polished concrete and matte chrome — wrong in a way
that looks subtly broken rather than obviously broken, which makes it hard to diagnose.

`PolyHavenLit` splits the ARM map and inverts the roughness channel for you:

```
ARM ─┬─ R ──────────────────→ Ambient Occlusion
     ├─ G ── One Minus ─────→ Smoothness      (roughness → smoothness)
     └─ B ──────────────────→ Metallic
```

It is a single self-contained graph rather than a graph plus a subgraph. The ARM split is
three nodes; factoring it out would add an asset-GUID dependency and a second file
without buying any reuse, since every Poly Haven material in the project uses this one
shader. If you later need the split inside a *different* graph, that is the point at
which converting it to a subgraph earns its keep.

**Tiling and Offset** are exposed as two Vector2 properties rather than one Vector4, so
the Inspector shows you X/Y pairs you can actually read, and they feed Shader Graph's
Tiling And Offset node directly.

---

## 8. Setting tiling from Poly Haven's physical size

This is the part that matters most for this project, and it is the part people usually
eyeball. Don't.

Every Poly Haven texture page states a real-world size, e.g. *"4m × 4m"*. That is how
much actual wall or floor one repeat of the image covers. The formula:

```
Tiling.x = surface width in metres  ÷  texture size in metres
Tiling.y = surface depth in metres  ÷  texture size in metres
```

### Worked examples

A **12 m × 2.4 m catwalk** dressed with a 4 m × 4 m metal texture:

```
Tiling.x = 12  / 4 = 3
Tiling.y = 2.4 / 4 = 0.6
```

A **2 m × 2 m concrete ledge** with the same 4 m texture:

```
Tiling = (0.5, 0.5)
```

A **50 m × 50 m rooftop** with a 2 m asphalt texture:

```
Tiling = (25, 25)
```

### Why this matters here specifically

Everything in this project is modelled in real-world metres because height accuracy is
the therapeutic mechanism. Texture scale is part of that. A concrete texture tiled so
each slab reads as 20 cm instead of 1 m makes the surface — and therefore the whole
building — read as smaller and closer than it is. The user's sense of how high up they
are comes substantially from familiar-size cues, and surface texel scale is one of them.
Getting tiling wrong quietly undermines the exact thing the scene is built to measure.

So: read the size off the Poly Haven page, measure your primitive, divide. Don't adjust
tiling until it "looks right" — looking right is the failure mode.

### Checking your work

A Unity default cube is 1 m. Drop one next to a textured surface: with correct tiling, a
4 m texture should show its full pattern across four cubes laid end to end.

### Non-square surfaces

If Tiling.x and Tiling.y differ a lot, the texture stretches. That is correct behaviour
for a long narrow object — a 12 × 2.4 m catwalk really does have 3 repeats one way and
0.6 the other. If the stretching looks wrong rather than just anisotropic, the UVs on
your primitive are probably normalised to 0–1 regardless of shape, which is what Unity's
built-in Plane and Quad do. Scale the object's UVs or use the tiling to compensate.

---

## 9. If something looks wrong

| Symptom | Likely cause |
| --- | --- |
| Surface far too shiny | ARM imported with sRGB on, or the map went into a smoothness slot without inversion. Check §4. |
| Bumps look like dents | You have `_nor_dx` instead of `_nor_gl`. Check the console for the importer's warning. |
| Shimmering/crawling detail when you move your head | Mip maps off. Should not happen under this pipeline — check the texture is actually under `Assets/Textures/`. |
| Everything looks chrome | `_arm` missing from the material, so the ARM slot falls back to its default grey and metallic reads 0.5. |
| Texture pattern too small / too large | Tiling computed from guesswork instead of §8. |
| Blurry at grazing angles on large floors | Expected with aniso at 1. Raise **Aniso Level** to 2–4 on that one texture; it is cheap for a handful of materials. |
| Import settings keep reverting | You are editing a texture outside `Assets/Textures/`, so the stamp never applied. |
