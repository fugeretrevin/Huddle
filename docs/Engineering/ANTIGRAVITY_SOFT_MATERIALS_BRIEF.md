# Brief for Antigravity: soft, non-plastic agent materials (optional, after rc20)

**Why:** the owner wore rc19: "the agents look a bit softer yes but still plasticy. should we add like soft textures?".
rc20 already made the agents' body and clothing materials matte (smoothness 0.28, no sky reflections). The next step is
texture: the look should read as soft vinyl toy / felt / clay, not glossy plastic.

**Deliver (small, safe for Quest 3):**
1. One tileable detail set per surface type, 512x512, PNG:
   - `Tex_SoftVinyl_N.png` (normal map, very fine orange-peel / clay grain, low strength), for the cream body
     (`Mat_CreamCeramic`) and the role colours (`Mat_Developer_*`, `Mat_Researcher_*`, `Mat_Tester_*`).
   - `Tex_Felt_N.png` + `Tex_Felt_Mask.png` (fine fibre, for clothing / caps), optional.
2. The 10 materials in `Logs/rc20_matte_applied.txt`, with the normal map assigned (URP Lit, Normal Map strength 0.25 to
   0.4, tiling so the grain is about 1 mm at agent scale). Keep smoothness at 0.25 to 0.32. Do not change base colours.
3. Check the agent meshes have usable UVs for a tiling map (no visible seams across the face). If a mesh has no UVs,
   say so; do not unwrap and re-export characters without the owner's OK.

**Do not:** add new shaders, change the visors/lenses/glows (they stay glossy on purpose), touch prefabs, scenes or scripts,
or raise texture memory beyond about 2 MB in total.

**Handback:** the PNGs and the changed `.mat` files in `Assets/MuseDesk/Art/`; a before/after screenshot of one agent.
Claude then runs the Play checks and the frame-time check before any build.
