using Brutal.Numerics;
using Brutal.OpenGlApi;
using Brutal.StbApi;
using static Brutal.OpenGlApi.OpenGl;

namespace BrutalFps.Engine;

public enum ShadeMode
{
    /// <summary>Lit by the scene lights, using the chosen <see cref="Material"/>.</summary>
    Lit = 0,

    /// <summary>Flat colour, no lighting or fog (HUD, muzzle flash, projectiles).</summary>
    Unlit = 2,

    /// <summary>Samples the bound portal texture in screen space (see <see cref="Renderer.DrawPortalSurface"/>).</summary>
    PortalView = 3,

    /// <summary>A flat-coloured round portal surface (portals seen through another portal).</summary>
    PortalGlow = 4,

    /// <summary>A monitor showing a camera's feed (see <see cref="Renderer.DrawScreen"/>).</summary>
    Screen = 7,

    /// <summary>A one-way mirror: the reflected view in screen space from the front, nothing from behind (see <see cref="Renderer.DrawMirror"/>).</summary>
    Mirror = 8,

    /// <summary>A prism sight's eyepiece ring over the whole screen (see <see cref="Renderer.DrawScopeRing"/>).</summary>
    ScopeRing = 9,

    /// <summary>Clear window glass, blended over what's behind it (see <see cref="Renderer.DrawGlass"/>).</summary>
    Glass = 10,

    /// <summary>A helicopter's blades as a blur, opacity per vertex (see <see cref="Renderer.DrawRotorBlur"/>).</summary>
    RotorBlur = 11,

    /// <summary>A picture shown exactly as it is, with its transparency (see <see cref="Renderer.DrawImage"/>): the BRUTAL logo on the splash screen.</summary>
    Image = 12,
}

/// <summary>
/// Surface types. Most have a tileable PBR texture set baked in Blender (see <see cref="TextureLibrary"/>); boxes and
/// code-built meshes are textured by world-space triplanar mapping, Blender models through their UVs. The draw call's
/// colour tints the surface (paint takes it fully, bare material just follows its brightness). On top of the textures,
/// world-space noise adds large-scale wear (water stains, grime streaks, oil, puddles) so big surfaces don't tile.
/// </summary>
public enum Material
{
    Plain = 0,
    Concrete = 1,      // stained, peeling painted concrete walls
    Floor = 2,         // dusty concrete floor with oil stains and joints
    RustyMetal = 3,    // steel eaten by rust
    Steel = 4,         // dark grimy steel with light rust spots
    Wood = 5,          // weathered, nailed planks
    Window = 6,        // bright, dirty glass in a steel grid (emissive)
    PaintedMetal = 7,  // chipped paint over rust
    Corrugated = 8,    // shipping container panels: ribs, paint, rust streaks
    Gravel = 9,        // yard ground: gravel, puddles, oil
    Sky = 10,          // sky (drawn on a big box): the HDR cubemap of the area it surrounds
    RedDirt = 11,      // outback ground: red earth, pebbles, dry stubble (the road is painted into it)
    DirtRoad = 12,     // the outback road: compacted dirt with wheel ruts
    Bush = 13,         // scrub foliage
    Gunmetal = 14,     // finished dark steel (pistol slide)
    Polymer = 15,      // stippled black polymer (pistol frame and grip)
    Skin = 16,         // the player's hand
    Cloth = 17,        // jacket sleeve, flag
    Galvanized = 18,   // corrugated galvanised iron (the outback shack, fences, sheds, tanks)
    Emissive = 19,     // glows in its colour (lamp bulbs, lights)
    Glass = 20,        // dark, glossy glass (shop and car windows): reflects the sky
    Custom = 21,       // a model's own texture set, used exactly as baked (the hand, tyres, grilles)
    Leaves = 22,       // alpha-cutout foliage cards with their own texture set: two-sided, lit through
    Snow = 23,         // wind-packed snow with sastrugi ridges (the Arctic ground)
    Ocean = 24,        // the open sea: procedural swells, reflects the sky (drawn on a big flat box)
    Rock = 25,         // dark frost-shattered rock with lichen (the Arctic's outcrops)
    MarinePaint = 26,  // well-kept ship's paint over welded plate, with thin rust weeps (the ship's hull and superstructure)
    Fleece = 27,       // brushed polyester fleece with pills (the sleeves): textured and tinted like cloth
    Denim = 28,        // twill denim (jeans): textured and tinted like cloth
    Canvas = 29,       // heavy cotton canvas (work trousers and jackets): textured and tinted like cloth
    Leather = 30,      // grained leather (boots, belts): textured and tinted like cloth
    Knit = 31,         // chunky knitwear (hoodies, beanies): textured and tinted like cloth
    Hair = 32,         // hair strands: textured and tinted like cloth
    Bark = 33,         // smooth tree bark (the jungle's trunks, buttresses and lianas): triplanar, takes the tint's hue, mossy in the jungle
    Carpet = 34,       // office carpet tiles (procedural): 50 cm tiles of fine stripes, alternate tiles turned a quarter
    CeilingTile = 35,  // suspended ceiling (procedural): 60 cm mineral-fibre tiles, pinholed, in a white T-bar grid
    Veneer = 36,       // office furniture veneer (procedural): fine straight wood grain, satin lacquer; takes the tint
    WallPaint = 37,    // smooth emulsion on plasterboard (procedural): faint roller texture, scuffs low down
    Brick = 38,        // weathered red brick in running bond (procedural): per-brick colour, recessed mortar, soot and salt
    Whitewash = 39,    // limewash over brick, flaking off in patches to the red brick underneath (procedural)
    Grass = 40,        // rough grass and weeds with dry, bare patches (procedural ground)
    Blockwork = 41,    // painted concrete blockwork (procedural): 40 x 20 cm blocks, shallow joints; weathered outdoors (the prison)
    RoofSlate = 42,    // slate roof (procedural): staggered courses with shadowed edges, lichen near the eaves (the prison)
}

/// <summary>What a scope shows the world as (see <see cref="Renderer.SetVision"/>).</summary>
public enum ScopeVision { Off = 0, Night = 1, Thermal = 2 }

/// <summary>How bullet wounds look on a skinned part: bare skin, cloth (frayed hole plus a spreading blood soak), or a hard item (hole only).</summary>
public enum WoundStyle { Skin = 0, Fabric = 1, Hard = 2 }

/// <summary>
/// OpenGL 4.1 renderer: one physically based shader for everything, one draw call per object. Lighting is done in
/// linear HDR and tone-mapped (ACES) at the end; outdoor areas are lit by their sun and by their Blender-rendered sky.
/// </summary>
public sealed class Renderer : IDisposable
{
    private const string VertexSource = """
        #version 410 core
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec2 aUV;
        layout(location = 3) in vec4 aTangent;
        layout(location = 4) in vec4 aJoints;   // skinned meshes only: four joint indices...
        layout(location = 5) in vec4 aWeights;  // ...and their weights

        uniform mat4 uModel;
        uniform mat4 uViewProjection;
        uniform vec4 uClipPlane; // used when rendering a portal's view: hides everything behind the exit portal
        uniform int uMode;
        uniform vec3 uCameraPos;
        uniform int uSkin;          // 1: blend each vertex between uModel and uSkinModel by its weight (|tangent.w| - 1)
        uniform mat4 uSkinModel;
        uniform mat4 uBones[60];    // uSkin == 2: joint skinning matrices (SkinnedModel), in the model's space
        uniform vec3 uViewOrigin;   // the view model: what's drawn is relative to the camera, which is here (else zero)
        uniform vec3 uProjectFrom;  // subtracted before projecting: the camera, when uViewProjection is relative to it (else zero)
        uniform int uVision;        // (as in the fragment shader: 2 = thermal, which smoke isn't drawn in)
        // A smoke screen's puffs are lit once each, not once a pixel (Renderer.DrawParticles): 1 while lighting them into
        // uSmokeLight (a texel a puff, by its index in aNormal.y), 2 while drawing them with that light; 0 otherwise.
        uniform int uParticleBake;
        uniform sampler2D uSmokeLight;
        const int SMOKE_LIGHT_SIZE = 32;   // Renderer.SmokeLightSize

        out vec3 vWorldPos;
        out vec3 vTexPos;           // the position before uViewOrigin is added: precise for the view model
        out vec3 vNormal;
        out vec2 vUV;
        out vec4 vTangent;
        out vec3 vBindPos;          // skinned characters: the position before skinning (wounds are placed there)
        out vec3 vBindNormal;       // skinned characters: the normal before skinning (only meaningful when uSkin == 2)

        void main()
        {
            vec4 world;
            vec4 bakeAt = vec4(0.0, 0.0, 0.0, 1.0);
            bool smokeGone = false;
            vBindPos = aPosition;
            vBindNormal = aNormal;
            if (uMode == 6)
            {
                // Particles (see Renderer.DrawParticles): aPosition is the centre, aTangent = colour + kind/alpha.
                // Streaks (sparks, tracers, trails: kinds 3, 4 and 7) run from the centre back along aNormal, aUV = (side x half
                // width, 0..1 along); everything else is a billboard facing the camera, aUV = corner x half size,
                // turned by aNormal.x radians.
                int kind = int(floor(aTangent.w));
                vec3 toCam = uCameraPos - uViewOrigin - aPosition;
                vec3 V = toCam / max(length(toCam), 1e-4);
                if (kind == 3 || kind == 4 || kind == 7)
                {
                    vec3 along = aNormal;
                    float len = length(along);
                    vec3 dir = len > 1e-5 ? along / len : vec3(0.0, 1.0, 0.0);
                    vec3 side = cross(dir, V);
                    float sl = length(side);
                    side = sl > 1e-4 ? side / sl : normalize(cross(dir, abs(dir.y) < 0.9 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0)));
                    world = vec4(aPosition + along * aUV.y + side * aUV.x, 1.0);
                    vUV = vec2(sign(aUV.x), aUV.y);
                }
                else
                {
                    vec3 right = cross(vec3(0.0, 1.0, 0.0), V);
                    float rl = length(right);
                    right = rl > 1e-4 ? right / rl : vec3(1.0, 0.0, 0.0);
                    vec3 up = cross(V, right);
                    float c = cos(aNormal.x), s = sin(aNormal.x);
                    vec2 q = vec2(c * aUV.x - s * aUV.y, s * aUV.x + c * aUV.y);
                    world = vec4(aPosition + right * q.x + up * q.y, 1.0);
                    vUV = sign(aUV);
                }
                vWorldPos = world.xyz;
                vNormal = V;
                vTangent = aTangent;
                if (kind == 8)
                {
                    // A smoke screen's puff. Up close it fades out, and is gone by the time the eye is well inside it:
                    // a puff you're standing in would cover the whole screen, dozens of them deep (the grey of being in
                    // the cloud is Renderer.DrawWhiteout's). A thermal imager doesn't see it at all.
                    float fade = smoothstep(0.3, 1.0, length(toCam) / max(abs(aUV.x), 1e-3));
                    if (uParticleBake != 1)
                    {
                        float alpha = clamp((aTangent.w - 8.05) / 0.9, 0.0, 1.0);
                        vTangent.w = 8.05 + 0.9 * alpha * fade;
                        smokeGone = fade <= 0.0 || uVision == 2;
                    }
                    ivec2 cell = ivec2(int(aNormal.y + 0.5) % SMOKE_LIGHT_SIZE, int(aNormal.y + 0.5) / SMOKE_LIGHT_SIZE);
                    if (uParticleBake == 1)
                    {
                        // Lighting it: its texel, shaded as its centre is lit.
                        vWorldPos = aPosition;
                        bakeAt = vec4((vec2(cell) + 0.5 + 0.5 * sign(aUV)) / float(SMOKE_LIGHT_SIZE) * 2.0 - 1.0, 0.0, 1.0);
                    }
                    else if (uParticleBake == 2)
                    {
                        vNormal = texelFetch(uSmokeLight, cell, 0).rgb;
                    }
                }
            }
            else
            {
                world = uModel * vec4(aPosition, 1.0);
                mat3 linear = mat3(uModel);
                if (uSkin == 1)
                {
                    // A sleeve: its cuff follows the hand (uSkinModel), the rest the arm (uModel); both are rigid.
                    float w = clamp(abs(aTangent.w) - 1.0, 0.0, 1.0);
                    world = mix(world, uSkinModel * vec4(aPosition, 1.0), w);
                    mat3 skin = mat3(uSkinModel);
                    vNormal = mix(linear * aNormal, skin * aNormal, w);
                    vTangent = vec4(mix(linear * aTangent.xyz, skin * aTangent.xyz, w), aTangent.w);
                }
                else if (uSkin == 2)
                {
                    // A skinned character: each vertex follows up to four joints by its weights.
                    mat4 skin = uBones[int(aJoints.x)] * aWeights.x + uBones[int(aJoints.y)] * aWeights.y
                              + uBones[int(aJoints.z)] * aWeights.z + uBones[int(aJoints.w)] * aWeights.w;
                    world = uModel * (skin * vec4(aPosition, 1.0));
                    mat3 lin = linear * mat3(skin);
                    vNormal = lin * aNormal;
                    vTangent = vec4(lin * aTangent.xyz, aTangent.w);
                }
                else
                {
                    // (The cofactors: the inverse transpose, up to a scale, which the fragment shader normalizes away. Correct under non-uniform scale.)
                    vNormal = mat3(cross(linear[1], linear[2]), cross(linear[2], linear[0]), cross(linear[0], linear[1])) * aNormal;
                    vTangent = vec4(linear * aTangent.xyz, aTangent.w);
                }
                vWorldPos = world.xyz;
                vUV = aUV;
            }
            vTexPos = vWorldPos;
            vWorldPos += uViewOrigin;
            gl_Position = uViewProjection * vec4(world.xyz - uProjectFrom * world.w, world.w);
            gl_ClipDistance[0] = dot(vec4(vWorldPos, 1.0), uClipPlane);
            if (uMode == 6 && uParticleBake == 1)
            {
                gl_Position = bakeAt;
                gl_ClipDistance[0] = 1.0;
            }
            else if (smokeGone)
            {
                gl_Position = vec4(2.0, 2.0, 2.0, 1.0);   // (all of the puff's corners: off screen, nothing to fill)
            }
        }
        """;

    private const string FragmentSource = """
        #version 410 core
        in vec3 vWorldPos;
        in vec3 vTexPos;
        in vec3 vNormal;
        in vec2 vUV;
        in vec4 vTangent;
        in vec3 vBindPos;
        in vec3 vBindNormal;

        uniform vec3 uColor;
        uniform int uSkin;               // 2 while drawing a skinned character
        uniform int uWoundStyle;         // 0: skin, 1: fabric (hole + blood soak), 2: hard (hole only)
        uniform int uWoundCount;         // bullet wounds on the character being drawn
        uniform vec4 uWounds[12];        // xyz: the wound's centre in the character's bind pose, w: hole radius
        uniform vec4 uWoundInfo[12];     // x: age (s), y: seed, z: 1 for an exit wound
        uniform int uMode;
        uniform int uMaterial;
        uniform mat4 uTextureSpace; // world -> the space surfaces are textured in (identity, or a moving object's own space)
        uniform int uViewModel;     // 1 while drawing the gun and hands (no snow on them, etc.)
        uniform int uStudio;        // 1 for a menu's picture (gun locker, inventory): fixed studio lights, not the level's
        uniform vec4 uFlash;        // a shot's flash of light: xyz position, w strength (0 = none)
        const int MAX_FLARES = 4;   // burning flares: FlareShadowAtlas.MaxFlares
        uniform vec4 uFlarePos[MAX_FLARES];     // xyz position, w range (below 0: one hanging high in the sky, lighting the ground within -w m of the point under it)
        uniform vec4 uFlareColor[MAX_FLARES];   // rgb irradiance 1 m away (in the sky: SKY_FLARE_HEIGHT m away), a its row in the shadow atlas (below 0: no shadows)
        uniform int uFlareCount;
        uniform sampler2DShadow uFlareShadow;
        uniform vec2 uFlareDepth;   // a face's depth from the distance along its axis: x + y / distance
        uniform vec2 uFlareDepthSky;   // the same for the far-reaching faces of one in the sky
        uniform vec2 uFlareTile;    // x: the tangent of half a face's field of view, y: a texel, as a fraction of a tile
        uniform vec3 uCameraPos;
        uniform vec3 uFogColor;

        // Texture set of the current material (units 3-5), and the road's (units 6-8).
        uniform int uTextured;
        uniform int uCutout;         // 1: alpha-tested leaves (albedo alpha = opacity), drawn two-sided
        uniform int uA2C;            // 1: rendering to the multisampled window, so cutout edges use alpha-to-coverage
        uniform int uUseUV;          // 1: the mesh's UVs (Blender models); 0: world-space triplanar
        uniform float uTile;         // metres per texture repeat (triplanar)
        uniform vec3 uRefTint;       // the tint the textures were made for: other tints scale their brightness
        uniform int uTintMode;       // 0: tint scales bare material + colours paint; 1: tint only colours paint
        uniform vec2 uRoughMetal;    // untextured surfaces
        uniform sampler2D uAlbedo;
        uniform sampler2D uNormalMap;
        uniform sampler2D uOrm;
        uniform int uHasRoad;
        uniform float uRoadTile;
        uniform sampler2D uRoadAlbedo;
        uniform sampler2D uRoadNormal;
        uniform sampler2D uRoadOrm;

        // Skies (units 9-10): HDR cubemaps from Blender, scaled into the game's units.
        uniform int uSkyReady;
        uniform samplerCube uSkyYard;
        uniform samplerCube uSkyOutback;
        uniform float uSkyMips;
        uniform float uSkyScaleYard;
        uniform float uSkyDisplayYard;   // extra scale for the visible yard sky (darker at night than its ambient implies)
        uniform float uSkyScaleOutback;
        uniform vec3 uSunColorYard;      // sun irradiance in game units
        uniform vec3 uSunColorOutback;
        uniform float uExposure;

        // Hanging lamps: xyz = position, w = intensity.
        const int MAX_LIGHTS = 8;
        uniform vec4 uLights[MAX_LIGHTS];
        uniform int uLightCount;

        // Sun shadows (yard and outback)
        uniform int uShadowPass;         // 1 while rendering a shadow map (depth only)
        uniform int uShadowsReady;       // 1 once the shadow maps have been rendered
        uniform sampler2D uShadowYard;
        uniform sampler2D uShadowOutback;
        uniform mat4 uShadowYardVP;
        uniform mat4 uShadowOutbackVP;
        uniform vec3 uSunYard;           // directions towards the sun
        uniform vec3 uSunOutback;

        // Night floodlights in the container yard: spot lights with shadows (one 2 x 2 shadow atlas, unit 11),
        // and the lamps' glow in the hazy air.
        const int MAX_FLOODS = 4;
        uniform int uFloodCount;
        uniform vec4 uFloodPos[MAX_FLOODS];     // xyz position, w = cos(outer cone angle)
        uniform vec4 uFloodDir[MAX_FLOODS];     // xyz unit axis, w = cos(inner cone angle)
        uniform vec4 uFloodColor[MAX_FLOODS];   // rgb = colour x intensity (irradiance at 1 m)
        uniform mat4 uFloodVP[MAX_FLOODS];
        uniform sampler2DShadow uFloodShadow;
        uniform int uFloodShadowReady;
        const int MAX_GLOWS = 16;
        uniform int uGlowCount;
        uniform vec4 uGlow[MAX_GLOWS];          // xyz position, w intensity
        uniform vec4 uGlowColor[MAX_GLOWS];     // rgb colour, a = range it lights surfaces in (0: glow only)
        uniform float uGlowScatter;             // scattering coefficient x phase function of the night air
        uniform float uGlare;                   // strength of the halo round lamps in view

        // The container ship: its sky (unit 12) and its sun's shadow map (unit 13). The Arctic: a whiteout.
        uniform int uShipReady;
        uniform samplerCube uSkyShip;
        uniform float uSkyScaleShip;
        uniform vec3 uSunShip;
        uniform vec3 uSunColorShip;
        uniform sampler2D uShadowShip;
        uniform mat4 uShadowShipVP;
        uniform vec3 uWhiteout;                 // the Arctic's sky and blowing-snow colour (linear)

        // The jungle: a high sun through the canopy (its shadow map on unit 14), the clearing open to the sky.
        uniform int uJungleShadowReady;
        uniform sampler2D uShadowJungle;
        uniform mat4 uShadowJungleVP;
        uniform vec3 uSunJungle;
        uniform vec3 uSunColorJungle;
        uniform vec4 uJungleClearing;           // xy = the clearing's centre (x, z), zw = its radii

        // The factory grounds (area 8: round the warehouse, under the outback's sky and sun): their own sun shadow
        // map, on texture unit 16 where the GPU has one (otherwise no shadows outside the factory).
        uniform int uFactoryShadowReady;
        uniform sampler2D uShadowFactory;
        uniform mat4 uShadowFactoryVP;

        // The prison (two kilometres down, y < -1500): area 9 is its grounds and yard under a pale overcast with a soft
        // sun (its shadow map on texture unit 17 where the GPU has one), area 10 the rooms inside its buildings that
        // you can walk through (lit like the police station: ceiling fittings and bounce light).
        const int MAX_PRISON_ROOMS = 10;
        uniform int uPrisonRoomCount;
        uniform vec4 uPrisonRoomMin[MAX_PRISON_ROOMS];
        uniform vec4 uPrisonRoomMax[MAX_PRISON_ROOMS];
        uniform int uPrisonShadowReady;
        uniform sampler2D uShadowPrison;
        uniform mat4 uShadowPrisonVP;
        uniform vec3 uSunPrison;
        uniform vec3 uSunColorPrison;

        // The block of flats (three kilometres down, y < -2500): area 11 is outdoors and its undercroft, under the prison's sky
        // and sun (they're both in the Netherlands) with a shadow map of its own on texture unit 18; area 12 its stairwell.
        uniform int uFlatsShadowReady;
        uniform sampler2D uShadowFlats;
        uniform mat4 uShadowFlatsVP;
        uniform vec3 uFlatsStairMin;
        uniform vec3 uFlatsStairMax;
        const float FLATS_BELOW_Y = -2500.0;
        const float FLATS_FLOOR_Y = -3000.0;    // Level.FlatsOrigin.Y

        const float PRISON_BELOW_Y = -1500.0;
        const float PRISON_FLOOR_Y = -2000.0;   // Level.PrisonOrigin.Y

        // Rio de Janeiro (four kilometres down, y < -3500): area 13, a favela on a hillside. It takes the outback's sky
        // and sun, but turned about the vertical (uRioSkyTurn: cos, sin) so the sun stands where Rio's does (uSunRio),
        // with a shadow map of its own on texture unit 20 where the GPU has one.
        uniform int uRioShadowReady;
        uniform sampler2D uShadowRio;
        uniform mat4 uShadowRioVP;
        uniform vec3 uSunRio;
        uniform vec2 uRioSkyTurn;
        const float RIO_BELOW_Y = -3500.0;
        const float RIO_FLOOR_Y = -4040.0;      // 40 m below Level.RioOrigin, under the valley: a hillside has no one ground height, so nothing is darkened as if it stood on it

        // A direction in Rio, as the outback's sky cubemap has it.
        vec3 rioToOutbackSky(vec3 d)
        {
            return vec3(d.x * uRioSkyTurn.x - d.z * uRioSkyTurn.y, d.y, d.x * uRioSkyTurn.y + d.z * uRioSkyTurn.x);
        }

        // Inside one of the prison's walk-through rooms (its walls' inner faces are just inside the boxes).
        bool prisonInside(vec3 p)
        {
            for (int i = 0; i < MAX_PRISON_ROOMS; i++)
            {
                if (i >= uPrisonRoomCount)
                    break;
                if (all(greaterThanEqual(p, uPrisonRoomMin[i].xyz)) && all(lessThanEqual(p, uPrisonRoomMax[i].xyz)))
                    return true;
            }
            return false;
        }

        // Height above the floor of the area p is in (the prison is two kilometres down).
        float groundHeight(vec3 p) { return p.y < RIO_BELOW_Y ? p.y - RIO_FLOOR_Y : p.y < FLATS_BELOW_Y ? p.y - FLATS_FLOOR_Y : p.y < PRISON_BELOW_Y ? p.y - PRISON_FLOOR_Y : p.y; }

        // The police station (area 7): fluorescent ceiling panels and other office lights, each lighting only inside
        // its box. Pos: xyz, w = range; Color: rgb = colour x intensity, a = 1 for a ceiling panel (shines down only).
        // The occlusion map (unit 15) is a depth map from straight above of everything below the ceiling: the soft
        // shade under desks, chairs and cabinets that the panels and the room's bounce light don't reach.
        // The lights live in a uniform buffer (room for 256, rather than the few dozen plain uniforms leave), the
        // police station's first and the prison's after them: a point only loops over its own building's.
        const int MAX_OFFICE = 256;
        uniform int uOfficeCount;
        uniform int uOfficeSplit;               // the prison's lights start here (those before are the police station's)
        uniform int uOfficeRioStart;            // Rio's bulbs start here, to the end (only at night): lit in outdoorLight
        layout(std140) uniform OfficeLights
        {
            vec4 uOfficePos[MAX_OFFICE];
            vec4 uOfficeColor[MAX_OFFICE];
            vec4 uOfficeBoxMin[MAX_OFFICE];
            vec4 uOfficeBoxMax[MAX_OFFICE];
        };
        uniform vec3 uOfficeAmbient;            // bounce light off the walls, ceiling and carpet (irradiance)
        uniform int uOfficeOccReady;
        uniform sampler2D uOfficeOcc;
        uniform mat4 uOfficeOccVP;
        uniform float uOfficeOccRadius;         // the taps' spread, in the map's uv units

        // Local lights without shadows (a bulb, daylight down a vent, a portal's glow). Each lights only inside its
        // box. Pos: xyz, w = range; Color: rgb = colour x intensity.
        const int MAX_LOCAL = 12;
        uniform int uLocalCount;
        uniform vec4 uLocalPos[MAX_LOCAL];
        uniform vec4 uLocalColor[MAX_LOCAL];
        uniform vec4 uLocalBoxMin[MAX_LOCAL];
        uniform vec4 uLocalBoxMax[MAX_LOCAL];

        // Dim interiors in the outdoor areas (the shed, the room under it): inside a box the sky's light falls off
        // away from its opening. Min: xyz, w = what's left deep inside; Max: xyz; Opening: xyz, w = how far in it reaches.
        const int MAX_SHADE = 64;
        uniform int uShadeCount;
        uniform vec4 uShadeMin[MAX_SHADE];
        uniform vec4 uShadeMax[MAX_SHADE];
        uniform vec4 uShadeOpening[MAX_SHADE];

        uniform sampler2D uPortalTexture;
        uniform vec2 uScreenSize;

        // Water on portal surfaces
        const int MAX_RIPPLES = 16;
        uniform mat4 uPortalWorldToLocal;
        uniform vec4 uRipples[MAX_RIPPLES]; // xy = hit point in portal space (metres), z = start time, w = strength
        uniform int uRippleCount;
        uniform float uTime;
        uniform float uPortalRadius;
        uniform int uVision;        // what the view is seen as, through a scope: 0 as it is, 1 night vision, 2 thermal
        uniform int uParticleBake;  // smoke puffs: 1 while lighting them into a texture, 2 while drawing them with it (see the vertex shader)
        uniform float uBodyHeat;    // the character being drawn, in thermal: 1 alive, less once they're dead

        out vec4 fragColor;

        const float PI = 3.14159265;

        // ---------------------------------------------------------------- noise
        float hash13(vec3 p)
        {
            p = fract(p * 0.1031);
            p += dot(p, p.zyx + 31.32);
            return fract((p.x + p.y) * p.z);
        }

        float vnoise(vec3 p)
        {
            vec3 i = floor(p);
            vec3 f = fract(p);
            f = f * f * (3.0 - 2.0 * f);
            float a = mix(hash13(i), hash13(i + vec3(1, 0, 0)), f.x);
            float b = mix(hash13(i + vec3(0, 1, 0)), hash13(i + vec3(1, 1, 0)), f.x);
            float c = mix(hash13(i + vec3(0, 0, 1)), hash13(i + vec3(1, 0, 1)), f.x);
            float d = mix(hash13(i + vec3(0, 1, 1)), hash13(i + vec3(1, 1, 1)), f.x);
            return mix(mix(a, b, f.y), mix(c, d, f.y), f.z);
        }

        float fbm(vec3 p)
        {
            float sum = 0.0;
            float amp = 0.5;
            for (int i = 0; i < 5; i++)
            {
                sum += amp * vnoise(p);
                p = p * 2.03 + vec3(1.7, 9.2, 3.1);
                amp *= 0.5;
            }
            return sum;
        }

        float fbm3(vec3 p)
        {
            return 0.5 * vnoise(p) + 0.25 * vnoise(p * 2.03 + 1.7) + 0.125 * vnoise(p * 4.1 + 9.2);
        }

        vec3 toLinear(vec3 c) { return pow(max(c, vec3(0.0)), vec3(2.2)); }
        float luma(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

        // ------------------------------------------------------------- procedural fallbacks (no texture set)
        const vec3 RUST_DARK = vec3(0.26, 0.12, 0.05);
        const vec3 RUST_LIGHT = vec3(0.55, 0.27, 0.10);

        vec3 rustColor(vec3 p) { return mix(RUST_DARK, RUST_LIGHT, vnoise(p * 7.0)); }

        // Window glass: bright, dirty panes in a steel grid; a few panes broken or boarded. (Display space.)
        vec3 windowGlass(vec3 p, vec3 n, vec3 tint)
        {
            vec3 an = abs(n);
            vec2 uv = an.x > 0.5 ? p.zy : (an.z > 0.5 ? p.xy : p.xz);
            vec2 cell = uv / vec2(0.75, 0.6);
            vec2 f = abs(fract(cell) - 0.5);
            float bar = step(0.44, max(f.x, f.y));
            float pane = hash13(vec3(floor(cell), 3.0));
            float dirt = 0.55 + 0.45 * fbm(vec3(uv * 2.0, 1.0));
            vec3 glass = tint * dirt * (pane < 0.04 ? 0.1 : (pane < 0.16 ? 0.7 : 1.15));
            return mix(glass, vec3(0.05), bar);
        }

        // ------------------------------------------------------------- texture sampling
        struct Surface
        {
            vec3 albedo;   // linear
            float mask;    // paint
            vec3 n;        // shading normal (world)
            float ao;
            float rough;
            float metal;
        };

        vec3 unpackNormal(vec2 xy)
        {
            vec2 t = xy * 2.0 - 1.0;
            return vec3(t, sqrt(max(1.0 - dot(t, t), 0.0)));
        }

        // Triplanar: three planar projections blended by the normal. Each projection keeps texture "up" along +Y on
        // walls, so planks run level and container ribs stand upright. Normals use a whiteout-style blend.
        void triplanar(sampler2D A, sampler2D N, sampler2D O, vec3 pt, vec3 nt, float tile,
                       out vec4 albedo, out vec3 orm, out vec3 nOut)
        {
            vec3 w = pow(abs(nt), vec3(8.0));
            w /= (w.x + w.y + w.z);
            albedo = vec4(0.0);
            orm = vec3(0.0);
            vec3 offs = vec3(0.0);
            if (w.x > 0.01)
            {
                vec2 uv = pt.zy / tile;
                albedo += texture(A, uv) * w.x;
                orm += texture(O, uv).rgb * w.x;
                vec3 tn = unpackNormal(texture(N, uv).rg);
                offs += w.x * vec3(0.0, tn.y, tn.x);
            }
            if (w.y > 0.01)
            {
                vec2 uv = pt.xz / tile;
                albedo += texture(A, uv) * w.y;
                orm += texture(O, uv).rgb * w.y;
                vec3 tn = unpackNormal(texture(N, uv).rg);
                offs += w.y * vec3(tn.x, 0.0, tn.y);
            }
            if (w.z > 0.01)
            {
                vec2 uv = pt.xy / tile;
                albedo += texture(A, uv) * w.z;
                orm += texture(O, uv).rgb * w.z;
                vec3 tn = unpackNormal(texture(N, uv).rg);
                offs += w.z * vec3(tn.x, tn.y, 0.0);
            }
            nOut = normalize(nt + offs);
        }

        // Ground: a second, rotated and scaled sample blended in by a slow noise, so huge flat areas don't tile.
        void groundSample(sampler2D A, sampler2D N, sampler2D O, vec3 pt, float tile,
                          out vec4 albedo, out vec3 orm, out vec3 nOut)
        {
            vec2 uv1 = pt.xz / tile;
            vec2 uv2 = mat2(0.8, -0.6, 0.6, 0.8) * pt.xz / (tile * 1.37) + 0.31;
            float m = smoothstep(0.35, 0.65, fbm3(pt * (0.35 / tile) + 4.0));
            albedo = mix(texture(A, uv1), texture(A, uv2), m);
            orm = mix(texture(O, uv1).rgb, texture(O, uv2).rgb, m);
            vec3 t1 = unpackNormal(texture(N, uv1).rg);
            vec3 t2 = unpackNormal(texture(N, uv2).rg);
            t2.xy = mat2(0.8, 0.6, -0.6, 0.8) * t2.xy; // undo the rotation of the second sample
            vec3 tn = normalize(mix(t1, t2, m));
            nOut = normalize(vec3(tn.x, tn.z, tn.y));
        }

        bool isGround() { return uMaterial == 2 || uMaterial == 9 || uMaterial == 11 || uMaterial == 12 || uMaterial == 23; }

        Surface sampleSurface(vec3 p, vec3 pt, vec3 n, vec3 nt)
        {
            Surface s;
            vec4 a; vec3 orm; vec3 nn;
            if (uUseUV == 1)
            {
                a = texture(uAlbedo, vUV);
                orm = texture(uOrm, vUV).rgb;
                vec3 tn = unpackNormal(texture(uNormalMap, vUV).rg);
                vec3 T = vTangent.xyz - n * dot(n, vTangent.xyz);
                float tl = length(T);
                T = tl > 1e-5 ? T / tl : normalize(cross(n, abs(n.y) < 0.9 ? vec3(0, 1, 0) : vec3(1, 0, 0)));
                vec3 B = cross(n, T) * (vTangent.w < 0.0 ? -1.0 : 1.0);
                s.n = normalize(T * tn.x + B * tn.y + n * tn.z);
            }
            else
            {
                if (isGround() && nt.y > 0.9)
                    groundSample(uAlbedo, uNormalMap, uOrm, pt, uTile, a, orm, nn);
                else
                    triplanar(uAlbedo, uNormalMap, uOrm, pt, nt, uTile, a, orm, nn);
                s.n = normalize(transpose(mat3(uTextureSpace)) * nn); // texture space -> world (rigid)
            }

            // The texture's albedo is sRGB: the sampler decodes it. Alpha is the paint mask.
            // Bare material keeps the texture's colours but takes its overall brightness from the tint (the level's
            // art direction): the texture's average (its last mip) is scaled to the tint's brightness. Paint takes the
            // tint's colour outright.
            float mask = uMaterial == 16 || uMaterial == 22 ? 0.0 : a.a;
            vec3 tintLin = toLinear(uColor);
            if (uMaterial >= 21 && uMaterial < 27)
            {
                // A model's own texture set: used exactly as baked. On a "_tint" material the alpha is paint
                // coverage and the paint is the tint outright (so dust or dirt baked over it keeps its own colour).
                s.albedo = uTintMode == 1 ? mix(a.rgb, tintLin, mask) : mix(a.rgb, a.rgb * tintLin * 1.25, mask);
                s.mask = mask;
                s.ao = orm.r;
                s.rough = orm.g;
                s.metal = orm.b;
                return s;
            }
            vec3 average = textureLod(uAlbedo, vec2(0.5), 16.0).rgb;
            float target = luma(uTintMode == 0 ? tintLin : toLinear(uRefTint));
            vec3 bare = a.rgb * clamp(target / max(luma(average), 1e-3), 0.05, 4.0);
            // ...and leans a little towards the tint's hue (more for foliage, whose texture is fairly grey).
            vec3 hueTint = (uTintMode == 0 ? tintLin : toLinear(uRefTint)) / max(target, 1e-4);
            bare = mix(bare, luma(bare) * hueTint, uMaterial == 13 ? 0.6 : (uMaterial == 33 ? 0.8 : 0.3));
            vec3 paint = a.rgb * tintLin * 1.25;
            s.albedo = mix(bare, paint, mask);
            s.mask = mask;
            s.ao = orm.r;
            s.rough = orm.g;
            s.metal = orm.b;
            return s;
        }

        // ------------------------------------------------------------- large-scale wear on top of the textures
        // Bullet wounds on a character's skin, placed in its bind pose so they move with it: a dark hole with a ragged
        // edge, raw red flesh round it (wider and torn for an exit wound), a bruise, and wet blood trickling down from
        // it, lengthening over the first twenty seconds.
        // The same wounds on cloth (uWoundStyle 1) or a hard item (2): measured in the bind-space tangent plane of the
        // vertex, ignored when the wound is more than 4 cm off that plane (the other side of a limb). A frayed dark
        // hole of 1.3 x the wound radius; the fabric also soaks up blood in a stain that spreads, runs down and dries.
        void applyClothWounds(inout Surface s)
        {
            vec3 nb = normalize(vBindNormal);
            vec3 downV = vec3(0.0, -1.0, 0.0) - nb * dot(nb, vec3(0.0, -1.0, 0.0));
            float downLen = length(downV);
            vec3 down = downV / max(downLen, 1e-4);
            float runs = smoothstep(0.1, 0.4, downLen);   // on a horizontal surface there is no "down"
            for (int i = 0; i < uWoundCount; i++)
            {
                vec3 d = vBindPos - uWounds[i].xyz;
                float dn = dot(d, nb);
                if (abs(dn) > 0.04 || length(d) > 0.2)
                    continue;
                vec3 t = d - nb * dn;
                float R = uWounds[i].w;
                float age = uWoundInfo[i].x;
                float seed = uWoundInfo[i].y;
                bool exitW = uWoundInfo[i].z > 0.5;
                float r = length(t);
                float H = R * 1.3;
                float fray = vnoise(t * 900.0 + seed);
                float fine = vnoise(t * 2600.0 + seed * 1.7);
                float hole = 1.0 - smoothstep(H * (0.62 + 0.3 * fray), H * (0.9 + 0.35 * fray + 0.15 * fine), r);
                // loose threads: a pale fuzzy ring just outside the hole, broken up by the noise
                float rim = smoothstep(H * 0.8, H * 1.15, r) * (1.0 - smoothstep(H * 1.25, H * 1.9 + 0.002 * fine, r));
                if (uWoundStyle == 1)
                {
                    float rs = (0.006 + 0.045 * (1.0 - exp(-age / 12.0))) * (exitW ? 1.5 : 1.0);
                    float a = dot(t, down);
                    float perp = length(t - down * a);
                    float e = length(vec2(a > 0.0 ? mix(a, a / 1.6, runs) : a, perp));
                    float edge = 0.55 + 0.9 * vnoise(t * 380.0 + seed * 3.1) + 0.25 * fine;
                    float soak = 1.0 - smoothstep(rs * 0.45 * edge, rs * (0.95 + 0.2 * fray), e);
                    float wet = 1.0 - smoothstep(40.0, 90.0, age);
                    float dry = 1.0 - wet;
                    vec3 stain = mix(vec3(0.30, 0.018, 0.018), vec3(0.11, 0.018, 0.014), dry);
                    stain *= 1.0 - 0.2 * wet * (1.0 - smoothstep(rs * 0.2, rs * 0.3, e));   // pooling at the centre while wet
                    float amount = soak * (0.88 + 0.08 * dry);
                    s.albedo *= mix(vec3(1.0), stain, amount);
                    s.rough = clamp(s.rough - 0.3 * wet * soak, 0.05, 1.0);
                    s.rough = mix(s.rough, 0.9, dry * soak * 0.5);
                    s.albedo *= 1.0 + 0.25 * rim * fray * (1.0 - soak * 0.6);
                }
                s.albedo = mix(s.albedo, vec3(0.012, 0.010, 0.010), hole);
                s.rough = mix(s.rough, 0.9, hole);
                s.metal = mix(s.metal, 0.0, hole);
                s.ao *= 1.0 - 0.7 * hole;
            }
        }

        void applyWounds(inout Surface s)
        {
            if (uWoundStyle != 0)
            {
                applyClothWounds(s);
                return;
            }
            vec3 blood = vec3(0.16, 0.006, 0.005);
            for (int i = 0; i < uWoundCount; i++)
            {
                vec3 d = vBindPos - uWounds[i].xyz;
                float R = uWounds[i].w;
                float r = length(d);
                if (r > R * 7.0 + 0.13)
                    continue;
                float age = uWoundInfo[i].x;
                float seed = uWoundInfo[i].y;
                float tear = uWoundInfo[i].z > 0.5 ? 1.7 : 1.0;
                float n = vnoise(d * 1400.0 + seed);
                float hole = 1.0 - smoothstep(R * (0.55 + 0.3 * n), R * (0.8 + 0.35 * n), r / tear);
                float raw = 1.0 - smoothstep(R * 1.1, R * (1.9 + 0.9 * n) * tear, r);
                float bruise = 1.0 - smoothstep(R * 1.8, R * 6.0, r);
                float below = -d.y;
                float L = (0.012 + 0.11 * clamp(age / 20.0, 0.0, 1.0)) * (0.55 + 0.9 * fract(seed * 7.13));
                float wob = min(below / 0.015, 1.0) * 0.0028;
                float hd = length(d.xz + vec2(sin(below * 70.0 + seed), cos(below * 52.0 + seed * 2.3)) * wob);
                float width = R * 0.85 * (1.0 - 0.6 * smoothstep(0.0, L, below)) + 0.0007;
                float trickle = below > 0.0 && below < L ? (1.0 - smoothstep(width * 0.55, width, hd)) * (1.0 - smoothstep(L * 0.8, L, below)) : 0.0;
                s.albedo *= mix(vec3(1.0), vec3(0.74, 0.55, 0.55), bruise * 0.65);
                s.albedo = mix(s.albedo, blood * 1.7, raw * 0.85);
                s.albedo = mix(s.albedo, blood, max(trickle, raw * 0.6));
                s.albedo = mix(s.albedo, vec3(0.018, 0.0, 0.0), hole);
                s.rough = mix(s.rough, 0.2, max(max(raw, trickle), hole));
                s.metal = 0.0;
                s.ao *= 1.0 - 0.6 * hole;
            }
        }

        // ------------------------------------------------------------- Rio's walls
        // Rio's ground under a wall (world height): the terrace of the nearest alley, or partway up a block's stairs (its
        // 15 risers spread over the block's 7 m). Only roughly right away from the alleys, but enough to put the damp, the
        // moss and the splash-back at the foot of a wall rather than at one height across the whole hillside.
        const float RIO_ORIGIN_Y = -4000.0;     // Level.RioOrigin.Y
        float rioGround(vec3 p)
        {
            float z = clamp(p.z, -2.0, 63.0);
            float k = floor(z / 9.0);
            float t = clamp((z - 9.0 * k - 2.0) / 7.0, 0.0, 1.0);
            return RIO_ORIGIN_Y + 2.7 * (k + t);
        }

        // A favela wall: kind 0 painted brick (the tint is the paint), 1 bare brick (the tint is the brick), 2 grey render
        // or a concrete top. Hand-laid courses, not quite straight; paint soaked in brick by brick, faded by the sun and
        // flaking back to the brick; cracked and chipped bricks, long settlement cracks, grime washed down the wall,
        // damp, mud and green-black algae at the foot, and specks of dirt everywhere.
        void rioWall(inout Surface s, vec3 p, vec3 n, int kind)
        {
            vec3 an = abs(n);
            bool top = an.y > 0.5;
            if (top)
                kind = 2;
            vec2 bp = an.x > 0.5 ? vec2(p.z, p.y) : (an.z > 0.5 ? vec2(p.x, p.y) : p.xz);
            vec3 tu = an.x > 0.5 ? vec3(0.0, 0.0, 1.0) : vec3(1.0, 0.0, 0.0);
            vec3 tv = top ? vec3(0.0, 0.0, 1.0) : vec3(0.0, 1.0, 0.0);
            float hg = top ? 3.0 : p.y - rioGround(p);
            float px = length(fwidth(bp));
            float detail = 1.0 - smoothstep(0.004, 0.011, px);     // the brick courses (until a joint is under a pixel)
            float fine = 1.0 - smoothstep(0.0012, 0.0045, px);     // pits, bumps and hairlines
            vec3 tintLin = toLinear(uColor);

            // Bricks: 24 x 7 cm plus their joints, each course a little wavy and shifted by its own amount.
            vec2 cell = bp / vec2(0.25, 0.08);
            cell.y += (vnoise(vec3(bp.x * 1.3, 0.0, 4.0)) - 0.5) * 0.3;
            float row = floor(cell.y);
            cell.x += 0.5 * mod(row, 2.0) + (hash13(vec3(row, 3.0, 1.0)) - 0.5) * 0.35;
            vec2 id = vec2(floor(cell.x), row);
            vec2 f = fract(cell);
            float h = hash13(vec3(id, 11.0));
            float h2 = hash13(vec3(id, 23.0));
            vec2 e = min(f, 1.0 - f) * vec2(0.25, 0.08);
            float jw = 0.0035 + 0.002 * vnoise(vec3(bp * 25.0, 5.0)) + (kind == 1 ? 0.0015 + 0.003 * smoothstep(0.55, 0.8, vnoise(vec3(bp * 6.0, 7.0))) : 0.0);
            float mortar = kind == 2 ? 0.0 : mix(0.13, 1.0 - smoothstep(jw - 0.0015, jw + 0.0015, min(e.x, e.y)), detail);
            // Knocks along the bricks' edges: little chips out of the arris.
            float arris = kind == 2 ? 0.0 : (1.0 - smoothstep(jw, jw + 0.014, min(e.x, e.y))) * (1.0 - mortar) * detail;
            float edgeChip = arris * smoothstep(0.8, 0.86, vnoise(vec3(bp * 45.0, 27.0)) + 0.35 * (vnoise(vec3(bp * 4.0, 28.0)) - 0.5));
            // The brick's face is never flat: a mottle of the clay (or the paint over it) at a few centimetres.
            float mottle = mix(1.0, 0.86 + 0.28 * fbm3(vec3(bp * vec2(22.0, 30.0), 29.0)), fine);

            // Large-scale noise shared by the layers below.
            float fade = fbm3(vec3(bp * 0.35, 1.0));                  // sun-bleached and patchy paint
            vec2 warp = vec2(vnoise(vec3(bp * 14.0, 8.0)), vnoise(vec3(bp * 14.0, 9.0))) - 0.5;
            float flakeN = fbm3(vec3(bp * 4.5 + warp * 0.5, 6.0)) + 0.18 * (vnoise(vec3(bp * 40.0, 10.0)) - 0.5);
            float low = 1.0 - smoothstep(0.0, 1.2, hg);             // near the foot of the wall

            vec3 rawBrick = vec3(0.30, 0.10, 0.05) * mix(0.7, 1.25, h) * mix(vec3(1.0), vec3(1.1, 1.05, 0.9), step(0.85, fract(h * 13.0)));
            vec3 cement = vec3(0.28, 0.27, 0.25) * (0.85 + 0.3 * vnoise(vec3(bp * 40.0, 4.0)));
            vec3 c;
            float rough;
            if (kind == 0)
            {
                // Paint: each brick took it a little differently; the joints hold more, and sit in shadow.
                vec3 paint = tintLin * mix(1.0, 0.9 + 0.16 * h, detail);
                paint = mix(paint, vec3(luma(paint)) * 1.25 + paint * 0.15, 0.3 * smoothstep(0.4, 0.75, fade));
                paint *= (0.92 + 0.12 * vnoise(vec3(bp * 3.0, 2.0))) * mix(1.0, mottle, 0.6);
                paint = mix(paint, paint * 0.8, mortar);
                // Flaking, in ragged little patches (more low down, where the damp gets in, and where the sun has
                // bleached it), and knocked off the brick edges: underneath, grey cement, or the brick itself.
                float flakeT = 0.76 - 0.12 * low - 0.06 * smoothstep(0.55, 0.8, fade);
                float flake = max(smoothstep(flakeT, flakeT + 0.01, flakeN), edgeChip);
                float rim = smoothstep(flakeT - 0.025, flakeT, flakeN) * (1.0 - flake);
                vec3 under = mix(cement * 1.35, rawBrick * mottle, smoothstep(0.45, 0.6, vnoise(vec3(bp * 6.0, 13.0))) * (1.0 - mortar));
                c = mix(paint, under, flake);
                c *= 1.0 - 0.22 * rim * fine;                      // the lifted edge of the paint casts a hairline shadow
                rough = mix(mix(0.62, 0.85, mortar), 0.9, flake);
            }
            else if (kind == 1)
            {
                // Bare brick: fired unevenly, some scorched dark, some pale; sloppy grey mortar.
                vec3 brick = tintLin * mix(0.65, 1.2, h) * mix(vec3(1.0), vec3(1.12, 0.88, 0.7), step(0.8, fract(h * 17.0)));
                brick *= mix(1.0, 0.55, step(0.93, fract(h * 29.0)));
                brick = mix(tintLin * 0.9, brick, detail) * mottle;
                brick = mix(brick, brick * vec3(0.7, 0.62, 0.55), edgeChip);      // a chipped edge: the darker, rougher inside of the brick
                c = mix(brick, cement, mortar);
                rough = mix(0.85, 0.95, mortar);
            }
            else
            {
                // Render or concrete: trowel swirls and patches of newer cement.
                c = tintLin * (0.86 + 0.22 * fbm3(vec3(bp * 1.8, 3.0))) * (0.95 + 0.1 * vnoise(vec3(bp * vec2(14.0, 5.0), 9.0)));
                c = mix(c, c * vec3(0.92, 0.93, 0.95), smoothstep(0.55, 0.6, vnoise(vec3(bp * 1.1, 15.0))));
                rough = 0.88;
            }
            float ao = 1.0 - (kind == 0 ? 0.3 : 0.45) * mortar;

            // Pits and bumps in the brick's face.
            float pits = smoothstep(0.72, 0.9, vnoise(vec3(bp * 95.0, 2.0))) * fine;
            c *= 1.0 - 0.28 * pits;
            float b0 = vnoise(vec3(bp * 45.0, 8.0));
            float bx = vnoise(vec3((bp + vec2(0.006, 0.0)) * 45.0, 8.0));
            float by = vnoise(vec3((bp + vec2(0.0, 0.006)) * 45.0, 8.0));
            vec2 bump = vec2(bx - b0, by - b0) * fine * (kind == 0 ? 0.9 : 1.4);

            float crackL = 0.0;
            if (kind != 2)
            {
                // Cracked bricks: one here and there, and whole runs of them where the wall has settled, the crack
                // running on diagonally from brick to brick and stepping along the joints between.
                float zone = smoothstep(0.66, 0.78, vnoise(vec3(bp * vec2(0.5, 0.8), 19.0))) * detail;
                vec2 q = (f - 0.5) * vec2(0.25, 0.08);
                float zoneAng = (vnoise(vec3(bp * 0.25, 25.0)) - 0.5) * 2.6;
                float ang = mix(fract(h * 7.31) * 3.1416, zoneAng, step(0.5, zone));
                vec2 dir = normalize(vec2(cos(ang), sin(ang) * 0.6));
                float cracked = step(h2, 0.035 + 0.5 * zone);
                float stepJ = (1.0 - smoothstep(jw * 0.25, jw * 0.6, min(e.x, e.y))) * step(hash13(vec3(id, 37.0)), zone * 0.85) * fine;
                crackL = max(crackL, stepJ);
                float along = dot(q, dir);
                float dl = abs(dot(q, vec2(-dir.y, dir.x)) + (vnoise(vec3(along * 11.0, id)) - 0.5) * 0.012 + (vnoise(vec3(along * 120.0, id + 3.0)) - 0.5) * 0.0015);
                float crackB = (1.0 - smoothstep(0.0005, 0.0016 + 0.002 * (1.0 - fine), dl)) * cracked * detail * (1.0 - mortar);
                crackL = max(crackL, crackB);
                c = mix(c, c * 0.18, crackB);
                ao *= 1.0 - 0.5 * crackB;

                // A chunk knocked out of a corner: rough raw brick in a shadowed hollow.
                vec2 corner = vec2(step(0.5, fract(h * 5.7)), step(0.5, fract(h * 3.3)));
                vec2 dq = (f - corner) * vec2(0.25, 0.08);
                float chipR = 0.025 + 0.035 * fract(h * 13.1);
                float chipD = length(dq * vec2(1.0, 1.7)) + (vnoise(vec3(bp * 140.0, 3.0)) - 0.5) * 0.012;
                float chip = (1.0 - smoothstep(chipR * 0.8, chipR, chipD)) * step(0.6, h2) * step(h2, 0.64 + 0.2 * zone) * detail;
                vec3 inside = rawBrick * (0.55 + 0.35 * vnoise(vec3(bp * 200.0, 6.0)));
                c = mix(c, inside, chip);
                ao *= 1.0 - 0.45 * chip * (1.0 - smoothstep(chipR * 0.5, chipR, chipD) * 0.6);
                rough = mix(rough, 0.97, chip);
                bump += (dq / max(length(dq), 1e-4)) * chip * 0.25 * (0.4 + 0.6 * fine);
            }

            // In render and concrete, long meandering hairline cracks in some stretches. (Their width is kept about
            // constant on the wall: the noise's isoline over its gradient.)
            float cn = fbm3(vec3(bp * vec2(1.4, 0.9) + warp * 0.12, 17.0)) + 0.06 * vnoise(vec3(bp * 9.0, 18.0));
            float dCrack = abs(cn - 0.5) * px / max(fwidth(cn), 1e-6);
            float region = smoothstep(0.7, 0.78, vnoise(vec3(bp * 0.35, 19.0))) * smoothstep(0.35, 0.5, vnoise(vec3(bp * 2.0, 24.0)));
            float cw = mix(0.0006, 0.002, vnoise(vec3(bp * 3.0, 20.0)));
            if (kind == 2)
                crackL = (1.0 - smoothstep(cw * 0.5, cw + px * 0.8, dCrack)) * region * mix(0.35, 1.0, fine);
            c = mix(c, c * 0.12, crackL);
            ao *= 1.0 - 0.6 * crackL;
            rough = mix(rough, 1.0, crackL);

            // Grime washed down the wall from sills and eaves, and big damp stains.
            float streak = smoothstep(0.5, 0.85, vnoise(vec3(bp.x * 5.0, bp.y * 0.18, 5.0))) * (0.35 + 0.65 * fbm3(vec3(bp * 0.45, 1.0)));
            c = mix(c, c * vec3(0.52, 0.48, 0.42), streak * 0.55);
            float stain = smoothstep(0.55, 0.78, fbm3(vec3(bp * vec2(0.5, 0.35), 21.0)));
            c = mix(c, c * vec3(0.72, 0.66, 0.56), stain * 0.7);
            // General dirt: blotchy, a little browner, and nothing on the wall is quite the colour it was painted.
            float dirt = fbm3(vec3(bp * 1.6, 26.0));
            c *= mix(vec3(1.0), vec3(0.8, 0.76, 0.68), smoothstep(0.4, 0.75, dirt));

            // At the foot: damp darkening and splashed mud, then algae and moss, thickest in the joints and the cracks.
            float damp = 1.0 - smoothstep(0.0, 1.0, hg + (fbm3(vec3(bp * 2.2, 2.0)) - 0.5) * 0.6);
            c = mix(c, c * vec3(0.5, 0.47, 0.4), damp * 0.75);
            float mossN = fbm3(vec3(bp * vec2(2.2, 3.5), 12.0)) + 0.15 * vnoise(vec3(bp * 18.0, 14.0));
            float moss = smoothstep(0.5, 0.62, mossN + 0.35 * (1.0 - smoothstep(0.0, 0.7, hg)) + 0.15 * mortar + 0.25 * crackL - 0.12)
                       * (1.0 - smoothstep(0.15, 1.1, hg + (vnoise(vec3(bp.x * 3.0, 0.0, 22.0)) - 0.5) * 0.5));
            moss = max(moss, smoothstep(0.62, 0.7, mossN) * crackL);   // a little up the cracks, where water runs
            vec3 mossC = mix(vec3(0.025, 0.035, 0.010), vec3(0.08, 0.10, 0.025), vnoise(vec3(bp * 30.0, 16.0)));
            c = mix(c, mossC, moss * 0.92);
            rough = mix(rough, 0.97, moss);

            // Specks: flecks of dirt and mud all over (more near the ground), and a few pale chips and salt spots.
            float speckN = vnoise(vec3(bp * 70.0, 31.0));
            float speck = smoothstep(0.86 - 0.08 * low, 0.9 - 0.08 * low, speckN) * fine;
            c *= 1.0 - 0.5 * speck;
            float splash = smoothstep(0.78, 0.84, vnoise(vec3(bp * 22.0, 33.0))) * (1.0 - smoothstep(0.0, 0.5, hg)) * fine;
            c = mix(c, vec3(0.09, 0.07, 0.05), splash * 0.7);
            float pale = smoothstep(0.9, 0.93, vnoise(vec3(bp * 55.0, 35.0))) * fine;
            c = mix(c, vec3(0.5, 0.48, 0.44), pale * 0.45);

            s.albedo = c;
            s.ao = ao * (1.0 - 0.25 * moss);
            s.rough = rough;
            s.metal = 0.0;
            s.mask = kind == 0 ? 1.0 : 0.0;
            // Recessed joints, the bumps in the faces, and the cracks' shadowed edges.
            vec2 g = vec2(f.x < 0.5 ? -1.0 : 1.0, f.y < 0.5 ? -1.0 : 1.0) * (1.0 - smoothstep(jw, jw + 0.009, e)) * detail * (kind == 2 ? 0.0 : 1.0);
            s.n = normalize(n - (tu * g.x + tv * g.y) * 0.3 - (tu * bump.x + tv * bump.y) * 0.8);
        }

        // Rio's paving: cracked, patched, littered with grit, leaves and bits of rubbish, dark and mossy in the cracks.
        void rioFloor(inout Surface s, vec3 p, vec3 n)
        {
            vec2 q = p.xz;
            float px = length(fwidth(q));
            float fine = 1.0 - smoothstep(0.002, 0.007, px);
            float cn = fbm3(vec3(q * 0.9, 41.0));
            float dCrack = abs(cn - 0.5) * px / max(fwidth(cn), 1e-6);
            float crack = (1.0 - smoothstep(0.0012, 0.003 + px * 0.8, dCrack)) * smoothstep(0.5, 0.65, vnoise(vec3(q * 0.4, 42.0))) * mix(0.4, 1.0, fine);
            s.albedo = mix(s.albedo, vec3(0.02, 0.025, 0.012), crack * 0.85);
            s.ao *= 1.0 - 0.5 * crack;
            float patchN = smoothstep(0.62, 0.66, fbm3(vec3(q * 0.5, 43.0)));
            s.albedo *= mix(1.0, 1.18, patchN);                      // newer cement patched in
            float grime = smoothstep(0.4, 0.8, fbm3(vec3(q * 0.7, 44.0)));
            s.albedo *= mix(1.0, 0.62, grime);
            float grit = smoothstep(0.74, 0.82, vnoise(vec3(q * 60.0, 45.0))) * fine;
            s.albedo = mix(s.albedo, vec3(0.04, 0.035, 0.03), grit * 0.7);
            float mud = smoothstep(0.55, 0.75, fbm3(vec3(q * 2.5, 50.0)));
            s.albedo = mix(s.albedo, vec3(0.09, 0.07, 0.05), mud * 0.5);
            float pebble = smoothstep(0.88, 0.92, vnoise(vec3(q * 35.0, 46.0))) * fine;
            s.albedo = mix(s.albedo, vec3(0.32, 0.30, 0.27), pebble * 0.6);
            // Litter: dry leaves and scraps of paper and plastic, a few to a square metre.
            vec2 lc = floor(q / 0.35);
            float lh = hash13(vec3(lc, 47.0));
            vec2 lp = (fract(q / 0.35) - 0.5 - (vec2(hash13(vec3(lc, 48.0)), hash13(vec3(lc, 49.0))) - 0.5) * 0.5) * 0.35;
            float la = lh * 6.2832;
            lp = mat2(cos(la), sin(la), -sin(la), cos(la)) * lp;
            float leaf = (1.0 - smoothstep(0.85, 1.0, length(lp / vec2(0.05, 0.022)))) * step(lh, 0.3) * fine;
            float scrap = (1.0 - smoothstep(0.9, 1.0, max(abs(lp.x) / 0.045, abs(lp.y) / 0.03))) * step(0.93, lh) * fine;
            vec3 leafC = mix(vec3(0.20, 0.10, 0.03), vec3(0.30, 0.22, 0.06), fract(lh * 37.0));
            s.albedo = mix(s.albedo, leafC, leaf * 0.9);
            vec3 scrapC = fract(lh * 91.0) < 0.5 ? vec3(0.65, 0.63, 0.58) : toLinear(vec3(0.75, 0.2, 0.15));
            s.albedo = mix(s.albedo, scrapC, scrap * 0.85);
            s.rough = mix(s.rough, 0.95, max(grime * 0.5, leaf));
        }

        void addWear(inout Surface s, vec3 p, vec3 pt, vec3 n)
        {
            if (p.y < RIO_BELOW_Y && (uMaterial == 38 || uMaterial == 41))
            {
                vec3 col = uColor;
                float sat = (max(col.r, max(col.g, col.b)) - min(col.r, min(col.g, col.b))) / max(max(col.r, max(col.g, col.b)), 1e-3);
                int kind = uMaterial == 38 ? 1 : (sat < 0.09 && max(col.r, max(col.g, col.b)) < 0.75 ? 2 : 0);
                rioWall(s, p, n, kind);
                return;
            }
            if (p.y < RIO_BELOW_Y && uMaterial == 2 && n.y > 0.5)
                rioFloor(s, p, n);
            if (uMaterial == 1) // concrete walls: water stains, grime streaks from the top, a dirty band at the bottom
            {
                float stain = smoothstep(0.5, 0.75, fbm(pt * vec3(0.4, 0.25, 0.4) + 11.0));
                s.albedo *= mix(vec3(1.0), vec3(0.62, 0.56, 0.47), stain);
                float streak = smoothstep(0.55, 0.85, vnoise(vec3((pt.x + pt.z) * 5.0, pt.y * 0.18, 0.0)));
                s.albedo *= 1.0 - 0.35 * streak * smoothstep(0.0, 4.0, pt.y);
                s.albedo *= mix(0.45, 1.0, smoothstep(0.0, 1.4, pt.y + (fbm(pt * 2.5) - 0.5) * 0.8));
                s.rough = mix(s.rough, 0.6, stain * 0.5);
            }
            else if (uMaterial == 2) // floor: dust, oil stains (glossy) and expansion joints every 5 m
            {
                float dust = 0.45 * smoothstep(0.35, 0.7, fbm(pt * 0.22 + 5.0));
                s.albedo = mix(s.albedo, vec3(0.20, 0.18, 0.15), dust);
                s.rough = mix(s.rough, 0.95, dust);
                float oil = smoothstep(0.66, 0.72, fbm(pt * 0.6 + 7.0));
                s.albedo *= mix(1.0, 0.3, oil);
                s.rough = mix(s.rough, 0.25, oil);
                vec2 g = abs(fract(pt.xz / 5.0 + 0.5) - 0.5) * 5.0;
                float joint = 1.0 - smoothstep(0.012, 0.03, min(g.x, g.y));
                s.albedo *= 1.0 - 0.6 * joint;
                s.ao *= 1.0 - 0.5 * joint;
            }
            else if (uMaterial == 8) // container steel: rust creeping up from the bottom of every container
            {
                float h = mod(pt.y + 0.001, 2.59);
                float bottom = 1.0 - smoothstep(0.0, 0.45, h + (fbm(pt * 3.0) - 0.5) * 0.5);
                vec3 rust = toLinear(rustColor(pt)) * 0.9;
                s.albedo = mix(s.albedo, rust, bottom * 0.75);
                s.rough = mix(s.rough, 0.9, bottom);
                s.albedo *= 0.8 + 0.2 * fbm(pt * 0.25 + 2.0);
            }
            else if (uMaterial == 9) // gravel: puddles (wet, dark and mirror-like) and oil spills
            {
                // At night (floodlights on) it has been raining: more puddles, and wet gravel everywhere.
                float wet = uFloodCount > 0 ? 1.0 : 0.0;
                float pf = fbm(pt * 0.3 + 2.0);
                float puddle = smoothstep(0.6 - 0.07 * wet, 0.66 - 0.07 * wet, pf);
                float damp = max(smoothstep(0.5, 0.62, pf), 0.55 * wet);
                s.albedo *= mix(1.0, 0.45, damp);
                s.rough = mix(s.rough, 0.42, damp);
                s.albedo = mix(s.albedo, vec3(0.015, 0.016, 0.018), puddle * 0.9);
                s.rough = mix(s.rough, 0.04, puddle);
                s.n = normalize(mix(s.n, n, puddle));
                s.albedo *= mix(1.0, 0.5, smoothstep(0.68, 0.74, fbm(pt * 0.5 + 13.0)));
            }
            else if (uMaterial == 11) // red dirt: bleached and iron-dark patches
            {
                s.albedo *= mix(vec3(1.0), vec3(1.25, 1.18, 1.1), 0.5 * smoothstep(0.5, 0.75, fbm(pt * 0.06 + 3.0)));
                s.albedo *= mix(1.0, 0.75, smoothstep(0.6, 0.8, fbm(pt * 0.4 + 8.0)));
            }
            else if (uMaterial == 13) // foliage: paler, bluer tops; darker inside the clumps
            {
                s.albedo = mix(s.albedo, s.albedo * vec3(1.2, 1.25, 1.3), 0.5 * smoothstep(0.2, 1.0, n.y));
                s.ao *= mix(0.55, 1.0, smoothstep(-0.4, 0.6, n.y));
            }
            else if (uMaterial == 34) // carpet tiles: 50 cm squares of fine stripes, alternate tiles turned a quarter
            {
                vec2 g = p.xz / 0.5;
                vec2 cell = floor(g);
                vec2 f = fract(g);
                float turn = mod(cell.x + cell.y, 2.0);
                float along = mix(f.x, f.y, turn);
                float h = hash13(vec3(cell, 5.0));
                float stripe = 0.5 + 0.5 * sin(along * 6.2832 * 18.0 + vnoise(vec3(p.xz * 9.0, 1.0)) * 2.5);
                float fleck = vnoise(vec3(p.xz * 140.0, 2.0));
                s.albedo *= (0.78 + 0.22 * stripe) * (0.9 + 0.2 * fleck) * (0.94 + 0.12 * h);
                s.albedo *= 0.92 + 0.16 * fbm3(p * 0.4 + 3.0);                 // traffic wear and shading
                vec2 e = min(f, 1.0 - f);
                float seam = 1.0 - smoothstep(0.0, 0.012, min(e.x, e.y));
                s.albedo *= 1.0 - 0.18 * seam;
                s.rough = 0.95;
                s.metal = 0.0;
            }
            else if (uMaterial == 35) // ceiling tiles in a T-bar grid (60 cm)
            {
                vec2 g = p.xz / 0.6;
                vec2 e = min(fract(g), 1.0 - fract(g)) * 0.6;
                float bar = 1.0 - smoothstep(0.011, 0.014, min(e.x, e.y));
                float pin = smoothstep(0.62, 0.75, vnoise(vec3(p.xz * 160.0, 4.0)));
                float fissure = smoothstep(0.55, 0.7, vnoise(vec3(p.xz * vec2(38.0, 11.0), 6.0)));
                s.albedo *= mix((1.0 - 0.25 * pin) * (1.0 - 0.12 * fissure) * (0.95 + 0.05 * hash13(vec3(floor(g), 1.0))), 1.08, bar);
                s.rough = mix(0.95, 0.45, bar);
                s.ao *= 1.0 - 0.25 * (1.0 - smoothstep(0.014, 0.03, min(e.x, e.y))) * (1.0 - bar);
            }
            else if (uMaterial == 36) // veneer: fine grain along the longer run of the piece (x or z), satin lacquer
            {
                // (The station's desks and cabinets run along x: on their tops and fronts the grain does too.)
                float u = abs(n.x) > 0.5 ? p.z : p.x;
                float v = abs(n.y) > 0.5 ? p.z : p.y;
                float grain = vnoise(vec3(u * 3.0, v * 90.0, 3.0)) * 0.6 + vnoise(vec3(u * 0.8, v * 22.0, 9.0)) * 0.4;
                s.albedo *= 0.82 + 0.32 * grain;
                s.rough = 0.42 + 0.1 * grain;
                s.metal = 0.0;
            }
            else if (uMaterial == 37) // emulsion paint on plasterboard: a faint roller stipple, scuffs low down
            {
                s.albedo *= 0.97 + 0.05 * vnoise(p * 55.0);
                float scuff = smoothstep(0.62, 0.8, fbm3(p * vec3(3.0, 1.2, 3.0) + 2.0)) * (1.0 - smoothstep(0.1, 0.9, p.y));
                s.albedo *= 1.0 - 0.18 * scuff;
                s.rough = 0.85;
                s.metal = 0.0;
            }
            else if (uMaterial == 38 || uMaterial == 39) // brick in running bond; limewashed brick flaking back to red
            {
                // Brick coordinates on the face: along it (u) and up it (v); 23 x 7.5 cm per brick with its mortar.
                vec2 bp = abs(n.x) > 0.5 ? vec2(p.z, p.y) : (abs(n.z) > 0.5 ? vec2(p.x, p.y) : p.xz);
                vec2 cell = bp / vec2(0.23, 0.075);
                float row = floor(cell.y);
                cell.x += 0.5 * mod(row, 2.0);
                vec2 id = vec2(floor(cell.x), row);
                vec2 f = fract(cell);
                vec2 e = min(f, 1.0 - f) * vec2(0.23, 0.075);
                // Far off, the courses get finer than a pixel: fade them to the wall's average instead of shimmering. The
                // mortar lines (11 mm) go once a pixel is about as wide as they are; the bricks themselves (7.5 cm courses)
                // stay to much further off, so the wall doesn't visibly change a few metres in front of you.
                float fw = length(fwidth(bp));
                float mortarAa = max(fw, 0.0015);
                float mortar = 1.0 - smoothstep(0.0055 - mortarAa, 0.0055 + mortarAa, min(e.x, e.y));
                float detail = 1.0 - smoothstep(0.006, 0.016, fw);
                float brickDetail = 1.0 - smoothstep(0.015, 0.04, fw);
                mortar = mix(0.18, mortar, detail);
                float h = hash13(vec3(id, 7.0));
                vec3 tintLin = toLinear(uMaterial == 39 ? vec3(0.50, 0.22, 0.15) : uColor);   // (limewash: the wall's tint is the wash; the brick under it is red)
                vec3 brick = tintLin * mix(0.9, mix(0.62, 1.18, h), brickDetail) * mix(vec3(1.0), vec3(1.08, 0.86, 0.72), step(0.8, fract(h * 17.0)) * brickDetail);
                brick *= 1.0 + (0.3 * vnoise(vec3(bp * 40.0, 1.0)) - 0.15) * brickDetail;
                vec3 mortarColor = vec3(0.36, 0.34, 0.31);
                vec3 c = mix(brick, mortarColor, mortar);
                // Soot and grime washed down from above, and white salt bloom low down.
                float grime = smoothstep(0.45, 0.8, fbm3(vec3(bp.x * 0.5, bp.y * 0.12, 3.0)));
                c *= mix(1.0, 0.55, grime);
                c = mix(c, vec3(0.55, 0.53, 0.5), smoothstep(0.6, 0.85, fbm3(vec3(bp * 0.9, 9.0))) * (1.0 - smoothstep(0.0, 1.6, p.y < RIO_BELOW_Y ? 10.0 : p.y)) * 0.6);   // (none in Rio: its hillside has no one ground height)
                if (uMaterial == 39)
                {
                    // Limewash: off-white, flaking away in big patches (more low down, where the damp gets in).
                    // A crisp, ragged edge where the wash has flaked: soft only far off, where it would shimmer.
                    float washN = fbm3(vec3(bp * 0.45, 4.0)) + 0.15 * smoothstep(0.5, 4.0, p.y) + 0.035 * (vnoise(vec3(bp * 7.0, 5.0)) - 0.5) * brickDetail;
                    float washW = max(0.004, fwidth(washN));
                    float wash = smoothstep(0.44 - washW, 0.44 + washW, washN);
                    vec3 lime = vec3(0.70, 0.68, 0.62) * (0.9 + 0.1 * vnoise(vec3(bp * 12.0, 2.0)));
                    c = mix(c, lime * mix(1.0, 0.8, mortar * 0.5), wash);
                    mortar *= 1.0 - wash;
                }
                s.albedo = c;
                s.ao *= 1.0 - 0.45 * mortar;
                s.rough = mix(0.85, 0.95, mortar);
                s.metal = 0.0;
                // The mortar is recessed: tip the normal at the brick edges.
                vec2 g = vec2(f.x < 0.5 ? -1.0 : 1.0, f.y < 0.5 ? -1.0 : 1.0) * (1.0 - smoothstep(0.004, 0.012, e)) * detail;
                vec3 tu = abs(n.x) > 0.5 ? vec3(0.0, 0.0, 1.0) : (abs(n.z) > 0.5 ? vec3(1.0, 0.0, 0.0) : vec3(1.0, 0.0, 0.0));
                vec3 tv = abs(n.y) > 0.5 ? vec3(0.0, 0.0, 1.0) : vec3(0.0, 1.0, 0.0);
                s.n = normalize(s.n - (tu * g.x + tv * g.y) * 0.35);
            }
            else if (uMaterial == 41) // painted concrete blockwork: 40 x 20 cm blocks in running bond, shallow joints
            {
                // Block coordinates on the face (along it, up it); on a top face, the plan.
                float gy = groundHeight(p);
                vec2 bp = abs(n.x) > 0.5 ? vec2(p.z, gy) : (abs(n.z) > 0.5 ? vec2(p.x, gy) : p.xz);
                vec2 cell = bp / vec2(0.40, 0.20);
                float row = floor(cell.y);
                cell.x += 0.5 * mod(row, 2.0);
                vec2 f = fract(cell);
                vec2 e = min(f, 1.0 - f) * vec2(0.40, 0.20);
                float detail = 1.0 - smoothstep(0.0015, 0.004, length(fwidth(bp)));
                float joint = (1.0 - smoothstep(0.003, 0.0065, min(e.x, e.y))) * detail;
                float h = hash13(vec3(floor(cell.x), row, 3.0));
                // Paint over the blocks' open texture: tiny pits, a slightly different sheen block to block.
                float pits = smoothstep(0.7, 0.85, vnoise(p * 70.0)) * detail;
                s.albedo *= (0.95 + 0.06 * h) * (1.0 - 0.10 * pits) * (1.0 - 0.28 * joint);
                s.ao *= 1.0 - 0.35 * joint;
                s.rough = mix(0.55, 0.85, joint) + 0.08 * pits;
                s.metal = 0.0;
                // Outdoors: dirt washed down from the sills and the eaves, and splash-back near the ground.
                if (p.y < PRISON_BELOW_Y && !prisonInside(p))
                {
                    float streak = smoothstep(0.55, 0.85, vnoise(vec3(bp.x * 4.0, gy * 0.15, 5.0)));
                    s.albedo *= 1.0 - 0.18 * streak * (0.4 + 0.6 * fbm3(vec3(bp * 0.3, 1.0)));
                    s.albedo *= mix(0.72, 1.0, smoothstep(0.0, 0.7, gy + (vnoise(vec3(bp * 3.0, 2.0)) - 0.5) * 0.3));
                }
                else
                {
                    float scuff = smoothstep(0.62, 0.8, fbm3(vec3(bp * vec2(3.0, 1.2), 2.0))) * (1.0 - smoothstep(0.1, 0.9, gy));
                    s.albedo *= 1.0 - 0.15 * scuff;
                }
                vec2 g = vec2(f.x < 0.5 ? -1.0 : 1.0, f.y < 0.5 ? -1.0 : 1.0) * (1.0 - smoothstep(0.003, 0.01, e)) * detail;
                vec3 tu = abs(n.x) > 0.5 ? vec3(0.0, 0.0, 1.0) : vec3(1.0, 0.0, 0.0);
                vec3 tv = abs(n.y) > 0.5 ? vec3(0.0, 0.0, 1.0) : vec3(0.0, 1.0, 0.0);
                s.n = normalize(s.n - (tu * g.x + tv * g.y) * 0.25);
            }
            else if (uMaterial == 42) // roof slates: overlapping courses up the slope, staggered joints, moss at the eaves
            {
                // Courses run level (the slope rises along the face's horizontal normal), so they follow world height.
                float gy = groundHeight(p);
                vec2 hz = normalize(abs(n.x) > abs(n.z) ? vec2(0.0, 1.0) : vec2(1.0, 0.0));
                float along = dot(p.xz, hz);
                float course = gy / 0.11;
                float row = floor(course);
                float u = along / 0.30 + 0.5 * mod(row, 2.0);
                float fv = fract(course);
                float fu = fract(u);
                float detail = 1.0 - smoothstep(0.002, 0.006, length(fwidth(vec2(along, gy))));
                float h = hash13(vec3(floor(u), row, 9.0));
                float edge = (1.0 - smoothstep(0.0, 0.12, fv)) * detail;          // the shadowed lower edge of the course above
                float gap = (1.0 - smoothstep(0.0, 0.02, min(fu, 1.0 - fu))) * detail;
                s.albedo *= mix(1.0, 0.82 + 0.3 * h, detail) * (1.0 - 0.45 * edge) * (1.0 - 0.5 * gap);
                float lichen = smoothstep(0.62, 0.8, fbm3(vec3(p.xz * 0.6, 4.0))) * (1.0 - smoothstep(7.0, 9.5, gy));
                s.albedo = mix(s.albedo, vec3(0.16, 0.17, 0.10), lichen * 0.45);
                s.rough = 0.7 + 0.15 * h;
                s.metal = 0.0;
                s.ao *= 1.0 - 0.4 * edge;
                s.n = normalize(s.n + vec3(0.0, 1.0, 0.0) * edge * 0.2);
            }
            else if (uMaterial == 40) // grass: patchy green, dry yellow tufts, bare earth
            {
                float big = fbm3(vec3(p.xz * 0.08, 1.0));
                float dry = smoothstep(0.45, 0.7, fbm3(vec3(p.xz * 0.25, 5.0)));
                float bare = smoothstep(0.66, 0.78, big);
                vec3 green = mix(vec3(0.07, 0.15, 0.03), vec3(0.16, 0.26, 0.06), vnoise(vec3(p.xz * 3.0, 2.0)));
                vec3 c = mix(green, vec3(0.32, 0.29, 0.13), dry * 0.7);
                c *= 0.75 + 0.5 * vnoise(vec3(p.xz * 28.0, 3.0));   // blades and clumps
                c = mix(c, vec3(0.20, 0.16, 0.11), bare);
                s.albedo = c * (toLinear(uColor) / 0.2);
                s.rough = 0.95;
                s.metal = 0.0;
                s.ao = 0.85 + 0.15 * vnoise(vec3(p.xz * 9.0, 4.0));
            }
            else if (uMaterial == 5 || uMaterial == 3) // wood and rusty metal: dust on top faces
            {
                float dust = smoothstep(0.5, 0.9, n.y) * (0.5 + 0.5 * vnoise(pt * 4.0));
                s.albedo = mix(s.albedo, vec3(0.16, 0.145, 0.12), dust * 0.45);
                s.rough = mix(s.rough, 1.0, dust * 0.5);
            }
        }

        // The outback road is painted into the ground: its own texture set, wheel ruts, and soft edges.
        const float OUTBACK_X = 1500.0;
        const float ROAD_X = 3000.0;
        void paintRoad(inout Surface s, vec3 p, vec3 n)
        {
            float x = p.x - ROAD_X;
            if (uHasRoad == 0 || abs(x) > 4.5)
                return;
            vec4 a; vec3 orm; vec3 nn;
            groundSample(uRoadAlbedo, uRoadNormal, uRoadOrm, p, uRoadTile, a, orm, nn);
            vec3 road = a.rgb * (luma(toLinear(vec3(0.68, 0.41, 0.25))) / max(luma(textureLod(uRoadAlbedo, vec2(0.5), 16.0).rgb), 1e-3));
            float wander = (fbm(vec3(p.z * 0.01, 0.0, 2.0)) - 0.5) * 0.8;
            float ruts = 0.0;
            for (int i = -1; i <= 1; i += 2)
            {
                float d = abs(x - wander - float(i) * 1.15);
                ruts += 1.0 - smoothstep(0.15, 0.45, d);
            }
            road *= 1.0 - 0.22 * ruts;
            road *= 0.95 + 0.05 * sin(p.z * 9.0);
            float edge = smoothstep(2.6, 3.6, abs(x) + (vnoise(p * 0.8) - 0.5));
            s.albedo = mix(road, s.albedo, edge);
            s.n = normalize(mix(nn, s.n, edge));
            s.ao = mix(orm.r, s.ao, edge);
            s.rough = mix(mix(orm.g, 0.8, ruts * 0.5), s.rough, edge);
        }

        // ------------------------------------------------------------- lighting (linear HDR)
        float D_GGX(float NoH, float a)
        {
            float a2 = a * a;
            float d = NoH * NoH * (a2 - 1.0) + 1.0;
            return a2 / (PI * d * d);
        }

        float V_SmithJoint(float NoV, float NoL, float a)
        {
            float gv = NoL * sqrt(NoV * NoV * (1.0 - a * a) + a * a);
            float gl = NoV * sqrt(NoL * NoL * (1.0 - a * a) + a * a);
            return 0.5 / max(gv + gl, 1e-5);
        }

        vec3 F_Schlick(vec3 F0, float VoH)
        {
            return F0 + (1.0 - F0) * pow(1.0 - VoH, 5.0);
        }

        vec3 envBRDF(vec3 F0, float rough, float NoV)
        {
            const vec4 c0 = vec4(-1.0, -0.0275, -0.572, 0.022);
            const vec4 c1 = vec4(1.0, 0.0425, 1.04, -0.04);
            vec4 r = rough * c0 + c1;
            float a004 = min(r.x * r.x, exp2(-9.28 * NoV)) * r.x + r.y;
            vec2 AB = vec2(-1.04, 1.04) * a004 + r.zw;
            return F0 * AB.x + AB.y;
        }

        // One light: diffuse + GGX specular. E = irradiance arriving face-on.
        vec3 directLight(Surface s, vec3 V, vec3 L, vec3 E, float wrap)
        {
            float NoL_raw = dot(s.n, L);
            float NoL = max((NoL_raw + wrap) / (1.0 + wrap), 0.0);
            if (NoL <= 0.0)
                return vec3(0.0);
            vec3 H = normalize(L + V);
            float NoV = max(dot(s.n, V), 1e-4);
            float NoH = max(dot(s.n, H), 0.0);
            float VoH = max(dot(V, H), 0.0);
            float a = max(s.rough * s.rough, 0.002);
            vec3 F0 = mix(vec3(0.04), s.albedo, s.metal);
            vec3 F = F_Schlick(F0, VoH);
            vec3 spec = D_GGX(NoH, a) * V_SmithJoint(NoV, max(NoL_raw, 0.0), a) * F;
            vec3 diffuse = s.albedo * (1.0 - s.metal) * (1.0 - F) / PI;
            return (diffuse * NoL + spec * max(NoL_raw, 0.0)) * E;
        }

        // A menu's picture, lit the same whatever the level and the time of day: a warm key light from above left and
        // in front, a cool fill from the right, a rim from behind to pick out the outline, and a soft even ambient.
        // (The picture's camera looks down -Z with Y up, so these are in its view.)
        vec3 studioLight(Surface s, vec3 V)
        {
            vec3 c = directLight(s, V, normalize(vec3(-0.45, 0.75, 0.55)), vec3(1.0, 0.96, 0.9) * 4.5, 0.2)
                   + directLight(s, V, normalize(vec3(0.8, 0.15, 0.45)), vec3(0.75, 0.85, 1.0) * 1.6, 0.4)
                   + directLight(s, V, normalize(vec3(0.25, 0.45, -0.85)), vec3(1.0) * 3.0, 0.0);
            float NoV = max(dot(s.n, V), 1e-4);
            vec3 F0 = mix(vec3(0.04), s.albedo, s.metal);
            float up = s.n.y * 0.5 + 0.5;
            vec3 ambient = mix(vec3(0.22, 0.22, 0.24), vec3(0.42, 0.42, 0.45), up);
            c += s.albedo * (1.0 - s.metal) * ambient * s.ao;
            c += envBRDF(F0, s.rough, NoV) * ambient * 1.3 * s.ao;
            return c;
        }

        // 1 = in sunlight, 0 = in shadow. 3x3 percentage-closer filtering softens the edges.
        float shadowAt(sampler2D map, mat4 viewProjection, vec3 p, vec3 n, vec3 sunDir)
        {
            if (uShadowsReady == 0)
                return 1.0;

            vec3 q = p + n * 0.05 + sunDir * 0.02;
            vec4 clip = viewProjection * vec4(q, 1.0);
            vec3 sc = clip.xyz / clip.w * 0.5 + 0.5;
            if (any(lessThan(sc, vec3(0.0))) || any(greaterThan(sc, vec3(1.0))))
                return 1.0;

            // 3 x 3 bilinear PCF: each tap gathers the 2 x 2 texels round it, compares them and blends the results by
            // where the point falls between them, so shadow edges are smooth rather than stair-stepped.
            vec2 size = vec2(textureSize(map, 0));
            vec2 st = sc.xy * size - 0.5;
            vec2 f = fract(st);
            vec2 corner = (floor(st) + 1.0) / size;
            float z = sc.z - 0.0008;
            float lit = 0.0;
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    vec4 l = step(vec4(z), textureGather(map, corner + vec2(x, y) / size, 0));
                    lit += mix(mix(l.w, l.z, f.x), mix(l.x, l.y, f.x), f.y);
                }
            return lit / 9.0;
        }

        // Floodlight i's shadow (1 = lit), from its tile of the atlas, with 2 x 2 hardware-filtered taps.
        float floodShadow(int i, vec3 p, vec3 n, vec3 L)
        {
            if (uFloodShadowReady == 0)
                return 1.0;
            // Normal-offset bias, larger where the light grazes the surface (corrugated walls would streak).
            float NoL = clamp(dot(n, L), 0.0, 1.0);
            vec3 q = p + n * (0.05 + 0.16 * (1.0 - NoL)) + L * 0.04;
            vec4 clip = uFloodVP[i] * vec4(q, 1.0);
            if (clip.w <= 0.0)
                return 1.0;
            vec3 sc = clip.xyz / clip.w * 0.5 + 0.5;
            if (any(lessThan(sc.xy, vec2(0.0))) || any(greaterThan(sc.xy, vec2(1.0))) || sc.z >= 1.0)
                return 1.0;
            float texel = 1.0 / float(textureSize(uFloodShadow, 0).x);
            vec2 tile = vec2(float(i - (i / 2) * 2), float(i / 2));
            vec2 uv = (clamp(sc.xy, vec2(4.0 * texel), vec2(1.0 - 4.0 * texel)) + tile) * 0.5;
            float z = sc.z - 0.00012;
            float lit = texture(uFloodShadow, vec3(uv + vec2(-0.6, -0.6) * texel, z))
                      + texture(uFloodShadow, vec3(uv + vec2(0.6, -0.6) * texel, z))
                      + texture(uFloodShadow, vec3(uv + vec2(-0.6, 0.6) * texel, z))
                      + texture(uFloodShadow, vec3(uv + vec2(0.6, 0.6) * texel, z));
            return lit * 0.25;
        }

        // How much of the masts' floodlight reaches p (must match Level.YardFloodReach). Their visors cut the light off
        // above the stacks' second tier, so the tops of the stacks lie in shade, but for a few dim patches where a
        // lamp's edge still gets over (up-facing roofs darkest, the upper walls a little less so); and the quay along
        // the water, west of the yard's west block, gets only spill (it has its own masts' pools: Level.YardGlows).
        float yardFloodReach(vec3 p, vec3 geomN)
        {
            vec2 q = p.xz - vec2(520.0, 0.0);   // (from Level.YardOrigin)
            // (A roof is sunk a little below its corner castings, about 5.0 m on a stack two high, so the cut-off for
            // up-facing surfaces is lower than for walls and for people standing up there.)
            float facingUp = smoothstep(0.3, 0.8, geomN.y);
            float cut = mix(4.8, 3.9, facingUp);
            float up = smoothstep(cut - 0.5, cut + 0.5, p.y);
            float patchLit = smoothstep(0.64, 0.84, vnoise(vec3(q * 0.085, 7.3)));
            float roof = mix(0.03, 0.2, patchLit);
            float level = mix(0.3, roof, facingUp);
            float quay = mix(0.15, 1.0, smoothstep(-12.0, -6.5, q.x));
            return mix(1.0, level, up) * quay;
        }

        // All the floodlights on a surface: soft-edged cones, inverse-square falloff, shadows.
        vec3 floodLights(Surface s, vec3 p, vec3 V, vec3 geomN)
        {
            vec3 c = vec3(0.0);
            float reach = yardFloodReach(uViewModel == 1 ? uCameraPos : p, geomN);
            for (int i = 0; i < MAX_FLOODS; i++)
            {
                if (i >= uFloodCount)
                    break;
                vec3 toL = uFloodPos[i].xyz - p;
                float d2 = dot(toL, toL);
                vec3 L = toL * inversesqrt(d2);
                float cone = smoothstep(uFloodPos[i].w, uFloodDir[i].w, dot(-L, uFloodDir[i].xyz));
                if (cone <= 0.0 || (dot(geomN, L) <= -0.05 && uViewModel == 0))
                    continue;
                // (The gun and hands take the shadow at the player's eyes: see outdoorLight.)
                float sh = uViewModel == 1 ? floodShadow(i, uCameraPos, normalize(uFloodPos[i].xyz - uCameraPos), normalize(uFloodPos[i].xyz - uCameraPos))
                                           : floodShadow(i, p, geomN, L);
                if (sh <= 0.0)
                    continue;
                c += directLight(s, V, L, uFloodColor[i].rgb * (cone * sh * reach / (d2 + 1.0)), 0.0);
            }
            // Lamps that also light what's near them (no shadows; a smooth cut-off at their range, in .a).
            for (int i = 0; i < MAX_GLOWS; i++)
            {
                if (i >= uGlowCount)
                    break;
                float range = uGlowColor[i].a;
                vec3 toL = uGlow[i].xyz - p;
                float d2 = dot(toL, toL);
                if (range <= 0.0 || d2 >= range * range)
                    continue;
                float win = 1.0 - d2 / (range * range);
                // (The quay's masts, the long-range ones, have visors like the yard's: their pools stay on the ground,
                // not on the stacks' tops. The crane's short-range lamps light the crane round them.)
                if (range > 20.0)
                    win *= mix(1.0, 0.25, smoothstep(4.3, 5.3, p.y));
                c += directLight(s, V, toL * inversesqrt(d2), uGlowColor[i].rgb * (uGlow[i].w * win * win / (d2 + 1.0)), 0.0);
            }
            return c;
        }

        // The glare round a lamp at angle sqrt(a2) (radians): a hot core, a bloom of about a degree, and a soft skirt
        // of a few degrees that dies away quickly, so the sky between the lamps stays dark.
        float glareProfile(float a2)
        {
            float bloom = 1.0 + a2 / 4.0e-4;
            float skirt = 1.0 + a2 / 6.4e-3;
            return 3.0 / (1.0 + a2 / 1.6e-5) + 2.4 / (bloom * sqrt(bloom)) + 0.35 / (skirt * skirt);
        }

        // Light scattered towards the eye by the night air along the view ray (camera to tmax) from every glowing
        // lamp: the closed-form integral of an isotropic point light's inverse-square falloff along a line.
        // Plus glare: the bright halo round a lamp you can see (as in a camera or the eye), a narrow core and a
        // wider skirt, scaled by how much light reaches the eye; skipped where something stands in front of the lamp.
        vec3 glowScatter(vec3 ro, vec3 rd, float tmax)
        {
            vec3 air = vec3(0.0);
            vec3 glare = vec3(0.0);
            for (int i = 0; i < MAX_GLOWS; i++)
            {
                if (i >= uGlowCount)
                    break;
                vec3 oc = uGlow[i].xyz - ro;
                float d2 = dot(oc, oc);
                float b = dot(oc, rd);
                float hr2 = max(d2 - b * b, 0.0);
                // (The air also dims the light on its way out from the lamp: approximated by the ray's closest
                // approach, which keeps the glow hanging round the lamps instead of lighting the whole sky. Rays
                // passing more than 30 m from a lamp get nothing worth adding.)
                if (hr2 < 900.0)
                {
                    float h = sqrt(max(hr2, 0.09));
                    air += uGlowColor[i].rgb * (uGlow[i].w * (atan((tmax - b) / h) + atan(b / h)) / h * exp(-0.3 * h));
                }
                if (b > 0.0 && tmax * tmax > d2 - 2.0)
                {
                    float a2 = hr2 / d2;   // (angle from the lamp)^2, radians
                    float g = glareProfile(a2);
                    glare += uGlowColor[i].rgb * (uGlow[i].w / d2 * g);
                }
            }
            return air * uGlowScatter + glare * uGlare;
        }

        // The lamps' halos mirrored in wet ground (puddles): the glare round each lamp seen along the reflected ray,
        // hidden where the lamp can't see the puddle (floodlights: their cone and shadow map).
        vec3 glowReflection(vec3 p, vec3 n, vec3 R)
        {
            vec3 glare = vec3(0.0);
            for (int i = 0; i < MAX_GLOWS; i++)
            {
                if (i >= uGlowCount)
                    break;
                vec3 oc = uGlow[i].xyz - p;
                float d2 = dot(oc, oc);
                float b = dot(oc, R);
                if (b <= 0.0)
                    continue;
                float vis = 1.0;
                if (i < uFloodCount)
                {
                    vec3 L = oc * inversesqrt(d2);
                    vis = smoothstep(uFloodPos[i].w, uFloodDir[i].w, dot(-L, uFloodDir[i].xyz)) * floodShadow(i, p, n, L) * yardFloodReach(p, n);
                }
                float a2 = max(d2 - b * b, 0.0) / d2;
                float g = glareProfile(a2);
                glare += uGlowColor[i].rgb * (uGlow[i].w / d2 * g * vis);
            }
            return glare * uGlare;
        }

        // ------------------------------------------------------------- areas
        // The world's separate areas, by x: 0 warehouse < 100 <= 1 container yard < 1500 <= 2 outback < 4500 <=
        // 3 bunker < 5500 <= 4 Arctic < 9000 <= 5 container ship < 16000 <= 6 jungle; anywhere below y = -500, 7 the
        // police station (kept near x = 0, a kilometre down, so its fine detail has full float precision). Round the
        // warehouse (x < 100 but outside its walls and above its roof), 8 the factory grounds, in the open. Below
        // y = -1500, the prison (two kilometres down, also near x = 0): 9 its grounds and yard, 10 inside its rooms. Below
        // y = -2500, the block of flats: 11 outdoors and its undercroft, 12 its stairwell. Below y = -3500, 13 Rio's favela.
        const float YARD_X = 100.0;
        const vec3 FACTORY_INSIDE = vec3(20.98, 10.62, 30.98);   // the warehouse's inside: |x|, y, |z| below these
        int areaOf(vec3 p)
        {
            if (p.y < RIO_BELOW_Y)
                return 13;
            if (p.y < FLATS_BELOW_Y)
                return all(greaterThanEqual(p, uFlatsStairMin)) && all(lessThanEqual(p, uFlatsStairMax)) ? 12 : 11;
            if (p.y < PRISON_BELOW_Y)
                return prisonInside(p) ? 10 : 9;
            if (p.y < -500.0)
                return 7;
            if (p.x < 100.0)
                return abs(p.x) < FACTORY_INSIDE.x && p.y < FACTORY_INSIDE.y && abs(p.z) < FACTORY_INSIDE.z ? 0 : 8;
            return p.x < 1500.0 ? 1 : p.x < 4500.0 ? 2 : p.x < 5500.0 ? 3 : p.x < 9000.0 ? 4 : p.x < 16000.0 ? 5 : 6;
        }

        // The areas that are graded to moonlight (a bit for each: Renderer.SetNightAreas). Their sky gives a dim blue
        // light, and their sun, its shadow map and all, is the moon: as strong as the sun was, scaled down, so an
        // overcast that hid the sun hides the moon too.
        uniform int uNightAreas;
        bool nightArea(int area) { return ((uNightAreas >> area) & 1) != 0; }
        const vec3 NIGHT_SKY_TINT = vec3(0.50, 0.66, 1.0);
        const float NIGHT_SKY_LEVEL = 0.14;
        const vec3 MOON_TINT = vec3(0.62, 0.74, 1.0);
        const float MOON_LEVEL = 0.07;
        // The ship at night: what you see beyond it (sky, sea, the air between) is almost black. Its own decks keep the
        // sky's light, so it stands out against them under its working lights.
        const float SHIP_NIGHT_OUTSIDE = 0.12;

        // Areas under the sky (lit by a sun and a sky); the others are interiors with their own lights.
        bool outdoorArea(int area) { return area == 1 || area == 2 || (area >= 4 && area <= 6) || area == 8 || area == 9 || area == 11 || area == 13; }

        // The jungle's humid air (linear): the haze that fills the forest, greener and darker than the sky.
        const vec3 JUNGLE_HAZE = vec3(0.25, 0.30, 0.24);
        vec3 jungleHaze() { return nightArea(6) ? NIGHT_SKY_TINT * luma(JUNGLE_HAZE) * NIGHT_SKY_LEVEL * 0.6 : JUNGLE_HAZE; }

        // How open to the sky the jungle is at p: 1 out in the clearing, little under the canopy.
        float jungleOpen(vec3 p)
        {
            vec2 d = (p.xz - uJungleClearing.xy) / max(uJungleClearing.zw, vec2(1e-3));
            return mix(0.30, 1.0, 1.0 - smoothstep(0.85, 1.25, length(d)));
        }

        uniform vec3 uShadeBoxMin; // inside of the open container: dim, lit from its doors (+X end)
        uniform vec3 uShadeBoxMax;

        vec3 daySkyRadiance(vec3 dir, float lod, int area)
        {
            if (area == 4)
                return uWhiteout * (0.92 + 0.12 * clamp(dir.y, 0.0, 1.0));   // whiteout: the same soft white everywhere
            if (area == 5 && uShipReady == 1)
                return textureLod(uSkyShip, dir, lod).rgb * uSkyScaleShip;
            if (area == 6)
            {
                // A bright, hazy tropical sky (the canopy edges in it are geometry); below the horizon, the light
                // that comes back off the dark green forest floor.
                vec3 horizon = vec3(0.52, 0.58, 0.55);
                vec3 zenith = vec3(0.55, 0.65, 0.74);
                vec3 below = vec3(0.07, 0.085, 0.05);
                return dir.y >= 0.0 ? mix(horizon, zenith, sqrt(clamp(dir.y, 0.0, 1.0))) : mix(horizon * 0.5, below, clamp(-dir.y * 3.0, 0.0, 1.0));
            }
            if (area == 9 || area == 11)
            {
                // The prison: a high, pale overcast, brighter where the sun is behind it, grey-green land below.
                vec3 horizon = vec3(0.47, 0.49, 0.51);
                vec3 zenith = vec3(0.40, 0.44, 0.50);
                vec3 below = vec3(0.10, 0.11, 0.10);
                float t = clamp(dir.y, 0.0, 1.0);
                vec3 c = dir.y >= 0.0 ? mix(horizon, zenith, sqrt(t)) : mix(horizon * 0.6, below, clamp(-dir.y * 3.0, 0.0, 1.0));
                float sunSide = max(dot(dir, normalize(uSunPrison)), 0.0);
                return c * (1.0 + 0.35 * pow(sunSide, 4.0) * step(0.0, dir.y));
            }
            bool outback = area == 2 || area == 8 || area == 13;   // the factory grounds and Rio share the outback's sky
            if (area == 13)
                dir = rioToOutbackSky(dir);   // (turned, so its sun is where Rio's is)
            if (uSkyReady == 0)
            {
                float t = clamp(dir.y * 1.5, 0.0, 1.0);
                return outback ? mix(vec3(0.55, 0.62, 0.72), vec3(0.18, 0.32, 0.62), t) * 0.5
                               : mix(vec3(0.62, 0.64, 0.66), vec3(0.40, 0.46, 0.56), t) * 0.5;
            }
            return outback ? textureLod(uSkyOutback, dir, lod).rgb * uSkyScaleOutback
                           : textureLod(uSkyYard, dir, lod).rgb * uSkyScaleYard;
        }

        // The same sky under the moon: its clouds and its brightness over the horizon, dim and blue.
        vec3 skyRadiance(vec3 dir, float lod, int area)
        {
            vec3 c = daySkyRadiance(dir, lod, area);
            // (More in the jungle: the canopy takes most of it, and it has to stay playable under the trees.)
            return nightArea(area) ? NIGHT_SKY_TINT * luma(c) * NIGHT_SKY_LEVEL * (area == 6 ? 1.6 : 1.0) : c;
        }

        // The sun's irradiance in an area (at night, the moon's).
        vec3 sunColorOf(int area)
        {
            bool outback = area == 2 || area == 8 || area == 13;   // (the factory grounds and Rio: the outback's sun)
            vec3 sunE = outback ? uSunColorOutback : (area == 5 ? uSunColorShip : (area == 4 ? vec3(0.0) : (area == 6 ? uSunColorJungle : (area == 9 || area == 11 ? uSunColorPrison : uSunColorYard))));
            return nightArea(area) ? MOON_TINT * luma(sunE) * MOON_LEVEL : sunE;
        }

        // The night sky as you see it: darker overhead than the light it gives (and than the haze at the horizon, which
        // it meets), with stars wherever the day's sky shows blue between the clouds. No stars through the ship's overcast.
        vec3 nightSkyView(vec3 c, vec3 dir, int area)
        {
            c *= mix(1.0, 0.12, smoothstep(0.0, 0.4, dir.y));
            if (area == 5)
                return c * SHIP_NIGHT_OUTSIDE;
            if (dir.y <= 0.0)
                return c;
            vec3 day = daySkyRadiance(dir, 0.0, area);
            float clear = smoothstep(0.12, 0.4, (day.b - day.r) / max(day.b, 1e-3));
            vec3 g = dir * 240.0;
            float h = hash13(floor(g));
            float star = step(0.992, h) * (1.0 - smoothstep(0.05, 0.4, length(fract(g) - 0.5)));
            return c + vec3(0.9, 0.95, 1.1) * star * (0.08 + 1.2 * fract(h * 97.0)) * clear * smoothstep(0.02, 0.25, dir.y);
        }

        // Local lights: inverse-square falloff with a smooth cut-off at their range, only inside their boxes.
        vec3 localLights(Surface s, vec3 p, vec3 V)
        {
            vec3 c = vec3(0.0);
            for (int i = 0; i < MAX_LOCAL; i++)
            {
                if (i >= uLocalCount)
                    break;
                if (any(lessThan(p, uLocalBoxMin[i].xyz)) || any(greaterThan(p, uLocalBoxMax[i].xyz)))
                    continue;
                vec3 toL = uLocalPos[i].xyz - p;
                float d2 = dot(toL, toL);
                float r = uLocalPos[i].w;
                if (d2 >= r * r)
                    continue;
                float win = 1.0 - d2 / (r * r);
                c += directLight(s, V, toL * inversesqrt(d2), uLocalColor[i].rgb * (win * win / (d2 + 0.25)), 0.15);
            }
            return c;
        }

        // How much of the sky's light reaches p: less inside a dim interior, away from its opening.
        float interiorAmbient(vec3 p)
        {
            float k = 1.0;
            for (int i = 0; i < MAX_SHADE; i++)
            {
                if (i >= uShadeCount)
                    break;
                if (any(lessThan(p, uShadeMin[i].xyz)) || any(greaterThan(p, uShadeMax[i].xyz)))
                    continue;
                float d = length(p - uShadeOpening[i].xyz) / uShadeOpening[i].w;
                k = min(k, mix(uShadeMin[i].w, 1.0, exp(-d * d)));
            }
            return k;
        }

        // Inside a closed room among the shade volumes (ShadeVolume.NoSun), no sun at all: its thin walls and ceiling
        // would otherwise let the shadow map's bias leak it in along their edges and in specks across them.
        bool sunShutOut(vec3 p)
        {
            for (int i = 0; i < MAX_SHADE; i++)
            {
                if (i >= uShadeCount)
                    break;
                if (uShadeMax[i].w > 0.5 && all(greaterThanEqual(p, uShadeMin[i].xyz)) && all(lessThanEqual(p, uShadeMax[i].xyz)))
                    return true;
            }
            return false;
        }

        // Rio at night: the bare bulbs hung over its alleys and stairways (in the office lights' buffer, from
        // uOfficeRioStart on), each only inside its own alley's box, so none shows through the houses.
        vec3 rioBulbs(Surface s, vec3 p, vec3 V)
        {
            vec3 c = vec3(0.0);
            for (int i = uOfficeRioStart; i < uOfficeCount; i++)
            {
                if (any(lessThan(p, uOfficeBoxMin[i].xyz)) || any(greaterThan(p, uOfficeBoxMax[i].xyz)))
                    continue;
                vec3 toL = uOfficePos[i].xyz - p;
                float d2 = dot(toL, toL);
                float r = uOfficePos[i].w;
                if (d2 >= r * r)
                    continue;
                float win = 1.0 - d2 / (r * r);
                c += directLight(s, V, toL * inversesqrt(max(d2, 1e-6)), uOfficeColor[i].rgb * (win * win / (d2 + 0.3)), 0.25);
            }
            return c;
        }

        // Outdoors: the sun (with shadows) plus the sky cubemap for diffuse and specular ambient light.
        vec3 outdoorLight(Surface s, vec3 p, vec3 V, vec3 geomN, int area)
        {
            bool outback = area == 2 || area == 8 || area == 13;   // (the factory grounds and Rio: the outback's sun, their own shadow maps)
            vec3 sunDir = area == 13 ? uSunRio : outback ? uSunOutback : (area == 5 ? uSunShip : (area == 6 ? uSunJungle : (area == 9 || area == 11 ? uSunPrison : uSunYard)));
            vec3 sunE = sunColorOf(area);
            bool sunUp = max(sunE.r, max(sunE.g, sunE.b)) > 0.0;
            // The gun and hands take the sun's shadow where the player's eyes are, all of them at once: the shadow
            // maps' texels are centimetres across, and their edges (and the normal offset) would leave dark patches
            // over the hands that look like dirt.
            vec3 sp = uViewModel == 1 ? uCameraPos : p;
            vec3 sn = uViewModel == 1 ? sunDir : geomN;
            float shadow = !sunUp ? 0.0 : (area == 8 ? (uFactoryShadowReady == 1 ? shadowAt(uShadowFactory, uShadowFactoryVP, sp, sn, sunDir) : 1.0)
                                       : area == 13 ? (uRioShadowReady == 1 ? shadowAt(uShadowRio, uShadowRioVP, sp, sn, sunDir) : 1.0)
                                       : area == 9 ? (uPrisonShadowReady == 1 ? shadowAt(uShadowPrison, uShadowPrisonVP, sp, sn, sunDir) : 1.0)
                                       : area == 11 ? (uFlatsShadowReady == 1 ? shadowAt(uShadowFlats, uShadowFlatsVP, sp, sn, sunDir) : 1.0)
                                       : outback ? shadowAt(uShadowOutback, uShadowOutbackVP, sp, sn, sunDir)
                                       : area == 5 ? shadowAt(uShadowShip, uShadowShipVP, sp, sn, sunDir)
                                       : area == 6 ? (uJungleShadowReady == 1 ? shadowAt(uShadowJungle, uShadowJungleVP, sp, sn, sunDir) : jungleOpen(sp))
                                                   : shadowAt(uShadowYard, uShadowYardVP, sp, sn, sunDir));

            if (sunShutOut(p))
                shadow = 0.0;

            // Inside the open container: dim, getting lighter towards the open doors.
            float ambScale = interiorAmbient(p);
            if (area == 1 && all(greaterThan(p, uShadeBoxMin - 0.02)) && all(lessThan(p, uShadeBoxMax + 0.02)))
            {
                ambScale = mix(0.25, 0.8, smoothstep(uShadeBoxMin.x, uShadeBoxMax.x, p.x) * smoothstep(uShadeBoxMin.x - 2.0, uShadeBoxMax.x, p.x));
                shadow = 0.0;
            }

            vec3 color = sunUp ? directLight(s, V, sunDir, sunE, 0.0) * shadow : vec3(0.0);
            if (area == 1 && uFloodCount > 0)
                color += floodLights(s, p, V, geomN);
            color += localLights(s, p, V);
            if (area == 13)
                color += rioBulbs(s, p, V);
            if (area == 4)
                color += directLight(s, V, normalize(vec3(-0.35, 0.75, -0.55)), uWhiteout * 0.45, 0.3) * ambScale; // the glare's brighter side (dim in a container)

            float NoV = max(dot(s.n, V), 1e-4);
            vec3 F0 = mix(vec3(0.04), s.albedo, s.metal);
            // ~ average sky radiance around the normal. The gun and hands take theirs from higher up the sky: their
            // sides that face the player would see the player, not the dark ground at the horizon, and in the shade
            // those sides (all you see of the backs of the hands) went a muddy brown.
            vec3 irrN = uViewModel == 1 ? normalize(s.n + vec3(0.0, 0.8, 0.0)) : s.n;
            vec3 irr = skyRadiance(irrN, uSkyMips - 2.0, area);
            vec3 R = reflect(-V, s.n);
            vec3 spec = skyRadiance(R, s.rough * (uSkyMips - 3.0), area);
            if (area == 6)
            {
                // Under the canopy the sky is mostly leaves: less of its light, and what's left is green.
                float open = jungleOpen(sp);
                vec3 leafy = mix(vec3(0.62, 0.85, 0.5), vec3(1.0), open) * open;
                irr *= leafy;
                spec *= leafy;
            }
            float specOcc = clamp(pow(NoV + s.ao, exp2(-16.0 * s.rough - 1.0)) - 1.0 + s.ao, 0.0, 1.0);
            // Darker where things meet the ground.
            float contact = mix(0.6, 1.0, clamp(smoothstep(0.0, 0.8, groundHeight(p)) + step(0.5, geomN.y), 0.0, 1.0));
            color += (s.albedo * (1.0 - s.metal) * irr * s.ao + spec * envBRDF(F0, s.rough, NoV) * specOcc) * contact * ambScale;
            return color;
        }

        // Indoors (the warehouse): dim bounce light, cool daylight through the side windows, warm hanging lamps.
        const float INDOOR_LEVEL = 1.6;
        vec3 indoorLight(Surface s, vec3 p, vec3 V)
        {
            vec3 n = s.n;
            // The gun and hands take their bounce and window light from higher up, plus a little light off the
            // player's own body on the sides that face them: otherwise those sides (most of what you see of the
            // backs of the hands) face the dark far end of the hall and go nearly black.
            vec3 na = uViewModel == 1 ? normalize(n + vec3(0.0, 0.8, 0.0)) : n;
            // (At night only a little moonlight comes in at the windows, and there's less bounce: the lamps do the work.)
            bool night = nightArea(0);
            vec3 light = mix(vec3(0.07, 0.065, 0.06), vec3(0.20, 0.19, 0.17), 0.5 + 0.5 * na.y) * (night ? 0.55 : 1.0);
            vec3 day = night ? vec3(0.13, 0.17, 0.26) : vec3(0.62, 0.66, 0.70);
            light += day * 0.32 * max(dot(na, normalize(vec3(0.75, 0.55, 0.1))), 0.0);
            light += day * 0.32 * max(dot(na, normalize(vec3(-0.75, 0.55, -0.15))), 0.0);
            if (uViewModel == 1)
                light += vec3(0.11, 0.10, 0.09) * max(dot(n, V), 0.0);
            vec3 spec = vec3(0.0);
            float NoV = max(dot(n, V), 1e-4);
            float a = max(s.rough * s.rough, 0.002);
            vec3 F0 = mix(vec3(0.04), s.albedo, s.metal);
            for (int i = 0; i < MAX_LIGHTS; i++)
            {
                if (i >= uLightCount)
                    break;
                vec3 toLight = uLights[i].xyz - p;
                float d2 = dot(toLight, toLight);
                vec3 L = toLight * inversesqrt(d2);
                float wrap = max((dot(n, L) + 0.2) / 1.2, 0.0);
                float atten = uLights[i].w / (1.0 + d2 * 0.07);
                light += vec3(1.0, 0.76, 0.48) * atten * wrap;
                float NoL = max(dot(n, L), 0.0);
                vec3 H = normalize(L + V);
                spec += vec3(1.0, 0.76, 0.48) * atten * NoL * D_GGX(max(dot(n, H), 0.0), a) * V_SmithJoint(NoV, NoL, a) * F_Schlick(F0, max(dot(V, H), 0.0));
            }
            light *= mix(1.0, 0.45, smoothstep(6.0, 10.0, p.y)); // the roof space is darker
            // The factory's office (Level.FactoryOffice): walled off from the hall's lamps and windows, lit by its ceiling panels.
            if (all(greaterThan(p, vec3(-9.02, -0.1, 21.98))) && all(lessThan(p, vec3(9.02, 3.02, 30.02))))
                light = mix(vec3(0.22, 0.23, 0.23), vec3(0.60, 0.62, 0.61), 0.5 + 0.5 * na.y);
            // The light levels above are in display terms (as the old shader used them): bring them to linear.
            vec3 diffuse = s.albedo * (1.0 - s.metal) * pow(light, vec3(2.2)) * INDOOR_LEVEL * s.ao;
            vec3 env = vec3(0.035, 0.033, 0.03) * (night ? 0.35 : 1.0) * envBRDF(F0, s.rough, NoV) * s.ao;
            return diffuse + spec * INDOOR_LEVEL * 0.6 + env;
        }

        // The bunker: bare concrete lit only by daylight down its ceiling vent and the glow of its portals.
        vec3 bunkerLight(Surface s, vec3 p, vec3 V)
        {
            vec3 c = localLights(s, p, V);
            vec3 amb = vec3(0.034, 0.035, 0.038) * (0.7 + 0.3 * s.n.y);   // faint bounce off the concrete
            float NoV = max(dot(s.n, V), 1e-4);
            vec3 F0 = mix(vec3(0.04), s.albedo, s.metal);
            return c + s.albedo * (1.0 - s.metal) * amb * s.ao + amb * 0.5 * envBRDF(F0, s.rough, NoV) * s.ao;
        }

        // The police station's occlusion from straight above (1 = open to the ceiling): twelve taps on a disc round the
        // point, so the shade under a desk fades out softly past its edge, as under a ceiling full of diffuse panels.
        float officeOcclusion(vec3 p, vec3 n)
        {
            if (uOfficeOccReady == 0 || uViewModel == 1)
                return 1.0;
            vec3 q = p + n * 0.03;
            vec4 clip = uOfficeOccVP * vec4(q, 1.0);
            vec3 sc = clip.xyz / clip.w * 0.5 + 0.5;
            if (any(lessThan(sc, vec3(0.0))) || any(greaterThan(sc, vec3(1.0))))
                return 1.0;
            float z = sc.z - 0.00004;   // ~2 cm (the map's depth spans 500 m)
            float open = 0.0;
            for (int i = 0; i < 12; i++)
            {
                float a = float(i) * 2.39996;
                float r = uOfficeOccRadius * sqrt((float(i) + 0.5) / 12.0);
                open += step(z, texture(uOfficeOcc, sc.xy + vec2(cos(a), sin(a)) * r).r);
            }
            return open / 12.0;
        }

        // The police station: fluorescent panels in a suspended ceiling, bounce light off the pale walls, and the
        // soft shade under the furniture (no hard shadows: the light comes from everywhere above).
        // The flats' stairwell lamps flicker, each by itself, told apart by height (Level.FlatsLights: one a floor, 2.5 m above
        // it). Even floors hold steady with a faint buzz and the odd stutter; odd floors are failing, mostly out, in bursts.
        float flatsLampFlicker(float y)
        {
            float lamp = floor((y - FLATS_FLOOR_Y - 2.5) / 3.0 + 0.5);
            float t = uTime + lamp * 3.7;
            float chatter = step(0.45, hash13(vec3(floor(t * 22.0), lamp, 1.0)));
            if (mod(lamp, 2.0) > 0.5)
                return step(0.6, hash13(vec3(floor(t * 0.9), lamp, 2.0))) * mix(0.1, 1.0, chatter);
            float stutter = step(0.8, hash13(vec3(floor(t * 0.7), lamp, 3.0)));
            return mix(0.94 + 0.06 * sin(t * 61.0), mix(0.15, 1.0, chatter), stutter);
        }

        vec3 officeLight(Surface s, vec3 p, vec3 V, vec3 geomN)
        {
            // (The flats' stairwell is a dim concrete shaft: next to no bounce.)
            vec3 officeAmbient = p.y < FLATS_BELOW_Y ? vec3(0.022, 0.022, 0.024) : uOfficeAmbient;
            float occ = officeOcclusion(p, geomN);
            vec3 c = vec3(0.0);
            int first = p.y < PRISON_BELOW_Y ? uOfficeSplit : 0;
            int last = p.y < PRISON_BELOW_Y ? uOfficeRioStart : uOfficeSplit;
            for (int i = first; i < last; i++)
            {
                if (any(lessThan(p, uOfficeBoxMin[i].xyz)) || any(greaterThan(p, uOfficeBoxMax[i].xyz)))
                    continue;
                vec3 toL = uOfficePos[i].xyz - p;
                float d2 = dot(toL, toL);
                float r = uOfficePos[i].w;
                if (d2 >= r * r)
                    continue;
                vec3 L = toL * inversesqrt(max(d2, 1e-6));
                float win = 1.0 - d2 / (r * r);
                // A panel is a flat diffuse emitter facing down: brightest straight under it, nothing sideways.
                float emit = uOfficeColor[i].a > 0.5 ? max(L.y, 0.0) * 1.6 : 1.0;
                float shade = uOfficeColor[i].a > 0.5 ? mix(0.22, 1.0, occ) : 1.0;
                float flicker = p.y < FLATS_BELOW_Y ? flatsLampFlicker(uOfficePos[i].y) : 1.0;
                c += directLight(s, V, L, uOfficeColor[i].rgb * (flicker * emit * shade * win * win / (d2 + 0.35)), 0.2);
            }
            // Bounce: brighter from above (the lit ceiling) than from the carpet, and less of it under things. The
            // ceiling itself takes the light thrown back up off the desks and floor (more of it near the panels).
            vec3 na = uViewModel == 1 ? normalize(s.n + vec3(0.0, 0.8, 0.0)) : s.n;
            vec3 amb = officeAmbient * (0.8 + 0.2 * na.y) * mix(0.4, 1.0, occ);
            float NoV = max(dot(s.n, V), 1e-4);
            vec3 F0 = mix(vec3(0.04), s.albedo, s.metal);
            // What a glossy surface mirrors: the bright ceiling above, the darker room below.
            vec3 R = reflect(-V, s.n);
            vec3 env = mix(officeAmbient * 0.35, officeAmbient * 1.6, smoothstep(-0.2, 0.6, R.y)) * mix(0.5, 1.0, occ);
            c += s.albedo * (1.0 - s.metal) * amb * s.ao + env * envBRDF(F0, s.rough, NoV) * s.ao;
            c += localLights(s, p, V);
            return c;
        }

        // The Arctic: a whiteout. Fog swallows everything a few tens of metres away, streaks of drift race past on
        // the wind at several depths, and flakes whip by close to the eye.
        // None of it gets into a closed room (a NoSun shade volume: the Arctic's hollow container): the stretch of the
        // view ray inside one has no fog, drift or flakes, so you see none inside it, nor through its open door.
        // Returns how long that stretch is, and the stretch itself (from, to along the ray; to < from if none).
        float shelteredRay(vec3 ro, vec3 rd, float dist, out vec2 span)
        {
            span = vec2(1.0, 0.0);
            float len = 0.0;
            float reach = min(dist, 200.0);   // (only rooms close by: the other areas' are thousands of metres off)
            vec3 inv = 1.0 / mix(rd, vec3(1e-6), lessThan(abs(rd), vec3(1e-6)));
            for (int i = 0; i < MAX_SHADE; i++)
            {
                if (i >= uShadeCount)
                    break;
                if (uShadeMax[i].w < 0.5)
                    continue;
                vec3 t0 = (uShadeMin[i].xyz - ro) * inv;
                vec3 t1 = (uShadeMax[i].xyz - ro) * inv;
                vec3 tn = min(t0, t1);
                vec3 tf = max(t0, t1);
                float a = max(max(tn.x, tn.y), max(tn.z, 0.0));
                float b = min(min(tf.x, tf.y), min(tf.z, reach));
                if (b > a)
                {
                    len += b - a;
                    span = span.y < span.x ? vec2(a, b) : vec2(min(span.x, a), max(span.y, b));
                }
            }
            return len;
        }

        vec3 arcticAir(vec3 color, vec3 ro, vec3 rd, float dist)
        {
            const vec3 wind = vec3(0.944, 0.0, 0.33);
            vec2 sheltered;
            float f = 1.0 - exp(-(dist - shelteredRay(ro, rd, dist, sheltered)) * 0.036);
            float drift = 0.0;
            for (int k = 0; k < 4; k++)
            {
                float t = min(dist, 1.6 + 2.6 * float(k * k) + 1.4 * float(k));
                if (t >= sheltered.x && t <= sheltered.y)
                    continue;
                vec3 q = ro + rd * t - wind * (uTime * 16.0);
                float along = dot(q, wind);
                vec3 side = q - wind * along;
                float n = vnoise(vec3(along * 0.09, side.y * 1.3 + uTime * 0.6, (side.x - side.z) * 0.8));
                drift += smoothstep(0.5, 0.85, n) * (0.36 - 0.07 * float(k));
            }
            float flakes = 0.0;
            for (int k = 0; k < 3; k++)
            {
                float t = min(dist, 0.7 + 1.3 * float(k));
                if (t >= sheltered.x && t <= sheltered.y)
                    continue;
                vec3 q = ro + rd * t - wind * (uTime * (10.0 + 4.0 * float(k))) + vec3(0.0, uTime * 1.1, 0.0);
                vec3 g = q * 4.0;
                vec3 fr = fract(g) - 0.5;
                float h = hash13(floor(g) + float(k) * 17.0);
                float r = length(fr - wind * dot(fr, wind) * 0.55);  // drawn out along the wind
                // Flakes catch the light or are in shade: some brighter than the whiteout behind them, some greyer.
                flakes += step(0.88, h) * (1.0 - smoothstep(0.06, 0.14, r)) * (fract(h * 37.0) < 0.5 ? -0.6 : 1.0);
            }
            vec3 c = mix(color, uWhiteout, f);
            float near = smoothstep(1.2, 4.0, dist);   // the drift thins out right in front of you (your hands, a doorway)
            c = mix(c, uWhiteout * 1.06, clamp(drift * (0.2 + 0.8 * f), 0.0, 1.0) * near);
            return mix(c, flakes > 0.0 ? uWhiteout * 1.2 : uWhiteout * 0.72, clamp(abs(flakes) * 0.6, 0.0, 1.0) * near);
        }

        // The open sea: flat, with normals from travelling swells and chop. Reflects the sky (Fresnel), deep blue-green
        // below, the odd white cap, and haze towards the horizon.
        const float SEA_LEVEL = -11.0;   // Level.SeaLevel
        const float YARD_SEA_LEVEL = -2.0;   // Level.YardSeaLevel: the sea off the yard's quay...
        const float YARD_QUAY_X = 491.1;     // ...west of its edge (Level.YardOrigin.X + YardQuayX)
        vec3 oceanColor(vec3 p, vec3 viewDir, float dist, int area)
        {
            vec2 x = p.xz;
            vec2 g = vec2(0.0);
            // direction xy, wavelength (m), amplitude (m)
            const vec4 W[6] = vec4[6](vec4(0.95, 0.31, 64.0, 0.60), vec4(0.70, -0.71, 33.0, 0.34), vec4(0.20, 0.98, 18.0, 0.22),
                                      vec4(-0.60, 0.80, 9.5, 0.11), vec4(0.90, -0.43, 5.2, 0.06), vec4(-0.30, -0.95, 2.7, 0.035));
            for (int i = 0; i < 6; i++)
            {
                vec2 d = normalize(W[i].xy);
                float k = 6.2831853 / W[i].z;
                float ph = k * dot(d, x) - sqrt(9.81 * k) * uTime;
                g += d * (W[i].w * k * cos(ph));
            }
            float n0 = fbm3(vec3(x * 0.5, uTime * 0.3));
            g += vec2(fbm3(vec3((x + vec2(0.5, 0.0)) * 0.5, uTime * 0.3)) - n0, fbm3(vec3((x + vec2(0.0, 0.5)) * 0.5, uTime * 0.3)) - n0) * 0.6;
            g /= 1.0 + dist * 0.004;                 // calmer far away (no shimmering)
            vec3 nrm = normalize(vec3(-g.x, 1.0, -g.y));
            float NoV = max(dot(nrm, -viewDir), 0.0);
            float F = 0.02 + 0.98 * pow(1.0 - NoV, 5.0);
            vec3 R = reflect(viewDir, nrm);
            R.y = abs(R.y);
            vec3 up = skyRadiance(vec3(0.0, 1.0, 0.0), uSkyMips - 2.0, area);
            vec3 deep = vec3(0.012, 0.034, 0.045) * luma(up) * 2.6;
            vec3 c = mix(deep, skyRadiance(R, 1.0, area), F);
            float cap = smoothstep(0.80, 0.92, fbm3(vec3(x * 0.07, uTime * 0.08)) + length(g) * 0.25);
            c = mix(c, up * 0.75, cap * 0.35 / (1.0 + dist * 0.01));
            vec3 hazeDir = normalize(vec3(viewDir.x, 0.0, viewDir.z));
            c = mix(c, skyRadiance(hazeDir, 1.0, area), 1.0 - exp(-dist * 0.0022));
            return nightArea(area) ? c * SHIP_NIGHT_OUTSIDE : c;
        }

        // The air between the camera and a surface dist away along viewDir in the given area: haze and the lamps'
        // glow outdoors, the Arctic's whiteout, dust underground and indoors.
        uniform vec4 uClipPlane;   // (also in the vertex shader) in a portal's view: the exit portal's wall

        // In a portal's view the camera sits behind the exit portal's wall: the air only starts where the view ray
        // comes out of the portal (the air on the near side was crossed before reaching the portal).
        float airStart(vec3 viewDir)
        {
            float side = dot(uClipPlane.xyz, uCameraPos) + uClipPlane.w;
            float facing = dot(uClipPlane.xyz, viewDir);
            return side < 0.0 && facing > 1e-4 ? -side / facing : 0.0;
        }

        vec3 applyAir(vec3 color, vec3 viewDir, float dist, int area)
        {
            float start = min(airStart(viewDir), dist);
            vec3 ro = uCameraPos + viewDir * start;
            dist -= start;
            if (area == 4)
                return arcticAir(color, ro, viewDir, dist);
            if (area == 6)
                return mix(color, jungleHaze(), (1.0 - exp(-dist * 0.016)) * 0.85);   // thick, humid air
            if (area == 1 || area == 2 || area == 5 || area == 8 || area == 13)
            {
                vec3 hazeDir = normalize(vec3(viewDir.x, max(viewDir.y, 0.02) * 0.3, viewDir.z));
                vec3 haze = skyRadiance(hazeDir, uSkyMips - 4.0, area) * (area == 5 && nightArea(5) ? SHIP_NIGHT_OUTSIDE : 1.0);
                float f = area == 2 || area == 8 || area == 13 ? (1.0 - exp(-dist * 0.0035)) * 0.85
                        : area == 5 ? (1.0 - exp(-dist * 0.0022)) * 0.9
                                    : (1.0 - exp(-dist * 0.012)) * 0.6;
                color = mix(color, haze, f);
                if (area == 1)
                    color += glowScatter(ro, viewDir, dist);
                return color;
            }
            if (area == 3)
                return mix(color, vec3(0.006, 0.0065, 0.007), (1.0 - exp(-dist * 0.04)) * 0.4);   // a little dust in the air
            if (area == 7 || area == 10 || area == 12)
                return mix(color, uOfficeAmbient * 0.55, (1.0 - exp(-dist * 0.012)) * 0.25);       // clean, air-conditioned
            if (area == 9 || area == 11)
            {
                // A little damp haze over the prison, the colour of the overcast towards the horizon.
                vec3 hazeDir = normalize(vec3(viewDir.x, max(viewDir.y, 0.02) * 0.3, viewDir.z));
                return mix(color, skyRadiance(hazeDir, 0.0, area), (1.0 - exp(-dist * 0.0035)) * 0.6);
            }
            return mix(color, toLinear(uFogColor) * INDOOR_LEVEL * 0.9 * (nightArea(area) ? 0.12 : 1.0), (1.0 - exp(-dist * 0.035)) * 0.75);
        }

        // The jungle: the forest floor strewn with dead leaves, moss and shining mud; everything else damp and
        // splashed with mud low down, with a green film of algae on its upward faces.
        void jungleDamp(inout Surface s, vec3 p, vec3 n)
        {
            if (isGround() && n.y > 0.5)
            {
                // Local coordinates (from the clearing): the jungle is 20 km from the origin, where p.xz * 6 has only
                // a few bits of fraction left and the patterns below would come out blocky and regular.
                vec2 lp = p.xz - uJungleClearing.xy;
                vec3 pl = vec3(lp.x, p.y, lp.y);
                float moss = smoothstep(0.55, 0.75, fbm3(pl * 0.35 + 11.0));
                s.albedo = mix(s.albedo, vec3(0.045, 0.075, 0.025), moss * 0.6);

                // Dead leaves: three scattered layers of leaf shapes (pointed lenses with a midrib, each sized, placed
                // and turned at random in its cell), every layer on its own grid scale and angle so no lattice shows.
                // (These were round discs on one 6-per-metre grid: from standing height they read as a regular
                // pattern of sunspots.)
                float leaf = 0.0;
                vec3 leafColor = vec3(0.0);
                for (int layer = 0; layer < 3; layer++)
                {
                    float fl = float(layer);
                    float ga = 0.7 + fl * 1.9;
                    vec2 g = mat2(cos(ga), sin(ga), -sin(ga), cos(ga)) * lp * (4.1 + fl * 2.3) + fl * 17.3;
                    vec2 id = floor(g);
                    float h = hash13(vec3(id, 3.0 + fl));
                    if (h < 0.42)
                        continue;   // no leaf in this cell
                    float h2 = hash13(vec3(id, 9.0 + fl));
                    float h3 = hash13(vec3(id, 15.0 + fl));
                    float a = h * 31.0;
                    vec2 q = mat2(cos(a), sin(a), -sin(a), cos(a)) * (fract(g) - 0.5 - (vec2(h2, h3) - 0.5) * 0.28);
                    float len = 0.2 + 0.14 * h2;                 // half length, in cells (stays inside the cell)
                    float u = q.x / len;
                    float halfWidth = max(len * (0.3 + 0.2 * h3) * (1.0 - u * u), 1e-4);
                    float m = step(abs(u), 1.0) * (1.0 - smoothstep(halfWidth * 0.7, halfWidth, abs(q.y)));
                    float rib = 1.0 - 0.3 * (1.0 - smoothstep(0.0, 0.012, abs(q.y)));
                    vec3 c = mix(vec3(0.12, 0.065, 0.03), vec3(0.28, 0.17, 0.065), fract(h * 13.0)) * rib;
                    leafColor = mix(leafColor, c, m);            // later layers lie on top
                    leaf = max(leaf, m);
                }
                float litter = smoothstep(0.25, 0.55, fbm3(pl * 0.2 + 4.0)) * leaf;
                s.albedo = mix(s.albedo, leafColor, litter * 0.85);
                float mud = smoothstep(0.62, 0.72, fbm3(pl * 0.15 + 21.0)) * (1.0 - litter);
                s.albedo *= mix(1.0, 0.5, mud);
                s.rough = mix(s.rough, 0.1, mud);
                s.n = normalize(mix(s.n, n, mud));
            }
            else
            {
                if (uMaterial == 33)
                {
                    // Bark: moss and lichen in patches and streaks running down the trunk, thickest low down.
                    float streak = fbm3(vec3(p.x * 1.6, p.y * 0.25, p.z * 1.6) + 7.0);
                    float moss = smoothstep(0.5, 0.7, streak + 0.25 * (1.0 - smoothstep(0.0, 4.0, p.y)));
                    s.albedo = mix(s.albedo, vec3(0.05, 0.09, 0.025), moss * 0.75);
                    float lichen = smoothstep(0.68, 0.74, vnoise(p * 3.1 + 2.0));
                    s.albedo = mix(s.albedo, vec3(0.42, 0.44, 0.38), lichen * 0.5 * (1.0 - moss));
                    s.rough = mix(s.rough, 0.95, moss);
                }
                s.albedo *= mix(1.0, 0.65, 1.0 - smoothstep(0.0, 0.5, p.y));
                s.albedo = mix(s.albedo, s.albedo * vec3(0.7, 1.0, 0.55), smoothstep(0.5, 0.9, n.y) * 0.45 * (1.0 - s.metal));
            }
        }

        vec3 tonemap(vec3 x)
        {
            x *= uExposure;
            vec3 y = (x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14);   // ACES (Narkowicz fit)
            return pow(clamp(y, 0.0, 1.0), vec3(1.0 / 2.2));
        }

        // The same air over a colour that is already tone-mapped (a portal's view, a portal's glow): the air is linear
        // in the colour behind it, so work out how much of that colour gets through and what the air adds.
        vec3 airOverDisplay(vec3 shown, vec3 p)
        {
            float dist = length(p - uCameraPos);
            vec3 viewDir = (p - uCameraPos) / max(dist, 1e-4);
            int area = areaOf(p);
            vec3 added = applyAir(vec3(0.0), viewDir, dist, area);
            vec3 through = clamp(applyAir(vec3(1.0), viewDir, dist, area) - added, 0.0, 1.0);
            vec3 air = added / max(vec3(1.0) - through, vec3(1e-3));
            return mix(shown, tonemap(air), vec3(1.0) - through);
        }

        vec3 dither()
        {
            return vec3((hash13(vec3(gl_FragCoord.xy, uTime * 60.0)) - 0.5) / 255.0);
        }

        // ------------------------------------------------------------- portal water
        float waterHeight(vec2 p)
        {
            float h = 0.010 * sin(p.x * 3.1 + uTime * 1.3)
                    + 0.008 * sin(p.y * 4.3 - uTime * 1.7)
                    + 0.005 * sin((p.x + p.y) * 7.0 + uTime * 2.3);

            // Constant loop bound: some drivers mishandle uniform-array loops with a uniform bound.
            for (int i = 0; i < MAX_RIPPLES; i++)
            {
                if (i >= uRippleCount)
                    break;

                vec4 r = uRipples[i];
                float age = uTime - r.z;
                if (age < 0.0 || age > 3.0)
                    continue;

                float d = length(p - r.xy);
                float x = d - age * 0.65;
                float band = exp(-x * x * 56.0);
                float fade = exp(-age * 1.2) / (1.0 + d * 4.0);
                h += 0.025 * r.w * sin(x * 60.0) * band * fade;
            }

            return h;
        }

        // ------------------------------------------------------------- bullet holes (decals) and particles
        // Decals (mode 5): one quad per hole. uv = local coordinates (-1..1) plus a multiple of 4 that picks the hole's
        // random seed; tangent.xyz = the quad's x axis; tangent.w = kind + 0.05 + 0.9 x opacity. See BulletHoles.cs.
        float ringNoise(float a, float seed, float freq)
        {
            return vnoise(vec3(cos(a) * freq + seed * 13.0, sin(a) * freq, seed * 17.0));   // continuous round the circle
        }

        // The normal of a pit's wall: tilted towards the hole's centre (dir points away from it, in the quad's plane).
        vec3 pitNormal(vec3 N, vec3 T, vec3 B, vec2 dir, float slope)
        {
            return normalize(N - (T * dir.x + B * dir.y) * slope);
        }

        // The flash of a shot: warm light from the muzzle and the ejection port for a frame or two.
        vec3 flashLight(Surface s, vec3 p, vec3 V)
        {
            if (uFlash.w <= 0.0)
                return vec3(0.0);
            vec3 toL = uFlash.xyz - p;
            float d2 = dot(toL, toL);
            return directLight(s, V, toL * inversesqrt(max(d2, 1e-6)), vec3(1.0, 0.62, 0.30) * (uFlash.w / (d2 + 0.004)), 0.3);
        }

        // A flare's shadow (1 = lit): the face of its row of tiles that p falls on (FlareShadowAtlas.ViewProjection), 2 x 2 hardware-filtered taps.
        float flareShadow(int row, vec3 centre, vec3 p, vec3 n, vec3 L, bool sky)
        {
            float NoL = clamp(dot(n, L), 0.0, 1.0);
            vec3 d;
            if (sky)
            {
                // Hundreds of metres off, a texel spans a metre or so: the offset off the surface grows with it.
                float span = 2.0 * length(p - centre) * uFlareTile.x * uFlareTile.y;
                d = p + n * span * (1.0 + 2.0 * (1.0 - NoL)) + L * span - centre;
            }
            else
                d = p + n * (0.035 + 0.11 * (1.0 - NoL)) + L * 0.03 - centre;
            vec3 a = abs(d);
            float m;
            int face;
            vec2 uv;
            if (a.x >= a.y && a.x >= a.z)
            {
                m = a.x;
                face = d.x > 0.0 ? 0 : 1;
                uv = vec2(d.x > 0.0 ? d.z : -d.z, d.y);
            }
            else if (a.y >= a.z)
            {
                m = a.y;
                face = d.y > 0.0 ? 2 : 3;
                uv = vec2(d.y > 0.0 ? d.x : -d.x, d.z);
            }
            else
            {
                m = a.z;
                face = d.z > 0.0 ? 4 : 5;
                uv = vec2(d.z > 0.0 ? -d.x : d.x, d.y);
            }
            uv = uv / (m * uFlareTile.x) * 0.5 + 0.5;
            float texel = uFlareTile.y;
            uv = clamp(uv, vec2(1.5 * texel), vec2(1.0 - 1.5 * texel));
            const vec2 rows = vec2(6.0, float(MAX_FLARES));
            vec2 at = (uv + vec2(float(face), float(row))) / rows;
            vec2 tap = 0.7 * texel / rows;
            vec2 depth = sky ? uFlareDepthSky : uFlareDepth;
            float z = (depth.x + depth.y / m) * 0.5 + 0.5 - 0.00002;
            return (texture(uFlareShadow, vec3(at + vec2(-tap.x, -tap.y), z)) + texture(uFlareShadow, vec3(at + vec2(tap.x, -tap.y), z))
                  + texture(uFlareShadow, vec3(at + vec2(-tap.x, tap.y), z)) + texture(uFlareShadow, vec3(at + vec2(tap.x, tap.y), z))) * 0.25;
        }

        // A flare in the sky (Flares.SkyHeight): the light it puts on the ground is given in lux (Flares.SkyLux SKY_FLARE_HEIGHT m
        // under it), and lux go into the night's grading the way the eye takes them, compressed: to the game, irradiance goes
        // as lux to the SKY_FLARE_GAMMA. Inverse-square (lux) becomes 1 / d^(2 x SKY_FLARE_GAMMA).
        const float SKY_FLARE_HEIGHT = 100.0;
        const float SKY_FLARE_GAMMA = 0.37;

        // Burning flares: inverse-square falloff with a smooth cut-off at their range, and shadows once they've settled.
        // One in the sky lights only the outdoors (its shadows are drawn from its own zone alone), out to the edge of its area.
        vec3 flareLights(Surface s, vec3 p, vec3 V, vec3 geomN, bool outdoors)
        {
            vec3 c = vec3(0.0);
            for (int i = 0; i < MAX_FLARES; i++)
            {
                if (i >= uFlareCount)
                    break;
                vec3 toL = uFlarePos[i].xyz - p;
                float d2 = dot(toL, toL);
                float r = uFlarePos[i].w;
                bool sky = r < 0.0;
                float falloff;
                if (sky)
                {
                    if (!outdoors)
                        continue;
                    float q = dot(toL.xz, toL.xz) / (r * r);   // (out across the ground, as a fraction of its area's radius, squared)
                    if (q >= 1.0)
                        continue;
                    falloff = (1.0 - q * q) * pow(SKY_FLARE_HEIGHT * SKY_FLARE_HEIGHT / max(d2, 1.0), SKY_FLARE_GAMMA);
                }
                else
                {
                    if (d2 >= r * r)
                        continue;
                    float win = 1.0 - d2 / (r * r);
                    falloff = win * win / (d2 + 0.3);
                }
                vec3 L = toL * inversesqrt(max(d2, 1e-6));
                float sh = 1.0;
                if (uFlareColor[i].a >= 0.0)
                {
                    // (The gun and hands take the shadow at the player's eyes: see outdoorLight.)
                    if (uViewModel == 1)
                    {
                        vec3 toEye = normalize(uFlarePos[i].xyz - uCameraPos);
                        sh = flareShadow(int(uFlareColor[i].a + 0.5), uFlarePos[i].xyz, uCameraPos, toEye, toEye, sky);
                    }
                    else
                    {
                        sh = flareShadow(int(uFlareColor[i].a + 0.5), uFlarePos[i].xyz, p, geomN, L, sky);
                    }
                    if (sh <= 0.0)
                        continue;
                }
                c += directLight(s, V, L, uFlareColor[i].rgb * (sh * falloff), 0.1);
            }
            return c;
        }

        vec3 litBy(Surface s, vec3 p, vec3 V, vec3 geomN, int area)
        {
            bool outdoors = outdoorArea(area);
            vec3 c = outdoors ? outdoorLight(s, p, V, geomN, area)
                   : (area == 3 ? bunkerLight(s, p, V) : (area == 7 || area == 10 || area == 12 ? officeLight(s, p, V, geomN) : indoorLight(s, p, V)));
            return c + flashLight(s, p, V) + (uFlareCount > 0 ? flareLights(s, p, V, geomN, outdoors) : vec3(0.0));
        }

        vec4 shadeDecal()
        {
            float kindF = floor(vTangent.w);
            int k = int(kindF + 0.5);
            float opacity = clamp((vTangent.w - kindF - 0.05) / 0.9, 0.0, 1.0);
            vec2 cell = floor((vUV + 2.0) / 4.0);
            vec2 q = vUV - cell * 4.0;
            float seed = hash13(vec3(cell, 7.0));
            vec3 N = normalize(vNormal);
            vec3 T = vTangent.xyz - N * dot(N, vTangent.xyz);
            T = length(T) > 1e-4 ? normalize(T) : normalize(cross(N, abs(N.y) < 0.9 ? vec3(0, 1, 0) : vec3(1, 0, 0)));
            vec3 B = cross(N, T);
            float r = length(q);
            vec2 dir = r > 1e-4 ? q / r : vec2(1.0, 0.0);
            float a = atan(q.y, q.x);
            vec3 p = vWorldPos;
            float aa = clamp(fwidth(r) * 0.75, 0.004, 0.25);   // edges a pixel or so soft, so they don't shimmer

            Surface s;
            s.albedo = vec3(0.0);
            s.mask = 0.0;
            s.n = N;
            s.ao = 1.0;
            s.rough = 0.9;
            s.metal = 0.0;
            float alpha = 0.0;

            if (k == 0 || k == 1)
            {
                // Concrete / rock: a chipped crater round a dark hole, fresh broken stone inside it, a dusting of chips
                // round it, and (concrete) a few hairline cracks.
                float hole = 0.13 + 0.05 * ringNoise(a, seed + 1.7, 3.0);
                float edge = 0.5 + 0.26 * ringNoise(a, seed, 2.2) + 0.1 * ringNoise(a, seed + 3.1, 6.0);
                vec3 fresh = k == 0 ? vec3(0.33, 0.325, 0.31) : vec3(0.075, 0.075, 0.085);
                float grit = vnoise(vec3(q * 5.0, seed * 31.0));
                float inHole = 1.0 - smoothstep(hole - aa, hole + aa, r);
                float inCrater = 1.0 - smoothstep(edge - aa, edge + aa, r);
                float t = clamp((r - hole) / (edge - hole), 0.0, 1.0);
                float to = clamp((r - edge) / max(1.0 - edge, 0.05), 0.0, 1.0);
                float chips = smoothstep(0.7, 0.8, vnoise(vec3(q * 9.0, seed * 13.0))) * (1.0 - to);
                vec3 outer = mix(fresh * 1.05, fresh * 0.8, chips);
                s.albedo = mix(outer, fresh * (0.45 + 0.55 * t) * (0.8 + 0.4 * grit), inCrater);
                s.albedo = mix(s.albedo, vec3(0.012), inHole);
                s.n = normalize(mix(N, pitNormal(N, T, B, dir, 1.0 * (1.0 - 0.4 * t) + (grit - 0.5) * 0.5), inCrater));
                s.ao = mix(1.0, mix(0.45, 1.0, t), inCrater) * mix(1.0, 0.3, inHole);
                alpha = mix(0.22 * (1.0 - to) * (1.0 - to) + chips * 0.75, 1.0, inCrater);
                if (k == 0)
                {
                    float crack = 0.0;
                    for (int i = 0; i < 4; i++)
                    {
                        float fi = float(i);
                        float ang = seed * 6.283 + fi * 1.7 + hash13(vec3(cell, fi)) * 1.2;
                        vec2 d = vec2(cos(ang), sin(ang));
                        float along = dot(q, d);
                        float off = abs(dot(q, vec2(-d.y, d.x)) + 0.07 * (vnoise(vec3(along * 7.0, fi, seed * 9.0)) - 0.5));
                        float len = 0.6 + 0.35 * hash13(vec3(cell, fi + 9.0));
                        crack = max(crack, (1.0 - smoothstep(0.008, 0.008 + aa * 1.5, off)) * smoothstep(edge * 0.8, edge, along) * (1.0 - smoothstep(len * 0.7, len, along)));
                    }
                    s.albedo = mix(s.albedo, vec3(0.03), crack);
                    alpha = max(alpha, crack * 0.8);
                }
            }
            else if (k == 2 || k == 3)
            {
                // Steel: a lead-smeared dent, a ring polished bright by the impact; painted steel loses a jagged ring of
                // paint (primer and rusty metal underneath), bare steel gets a sooty heat ring.
                float dent = 0.24 + 0.04 * ringNoise(a, seed, 3.0);
                float ring = 0.45 + 0.08 * ringNoise(a, seed + 2.0, 4.0);
                float chip = 0.62 + 0.28 * ringNoise(a, seed + 5.0, 3.5) + 0.1 * vnoise(vec3(q * 8.0, seed * 3.0));
                float inDent = 1.0 - smoothstep(dent - aa, dent + aa, r);
                float inRing = 1.0 - smoothstep(ring - aa, ring + aa, r);
                float inChip = k == 3 ? 1.0 - smoothstep(chip - aa, chip + aa, r) : 0.0;
                float t = clamp((r - dent) / (ring - dent), 0.0, 1.0);
                float to = clamp((r - ring) / (1.0 - ring), 0.0, 1.0);
                // Outside the polished ring: chipped paint, or soot.
                float bare = smoothstep(0.5, 0.6, vnoise(vec3(q * 7.0, seed * 3.0)));
                vec3 outerAlb = k == 3 ? mix(vec3(0.20, 0.10, 0.06), vec3(0.32, 0.31, 0.30), bare) : mix(vec3(0.10, 0.09, 0.08), vec3(0.16, 0.13, 0.10), to);
                float outerA = k == 3 ? mix(0.2 * (1.0 - to), 1.0, inChip) : 0.55 * (1.0 - to) * (1.0 - to);
                // (Not fully metallic: bright bare steel picks up the lamps as well as reflecting, so it reads at night.)
                vec3 ringAlb = vec3(0.66, 0.66, 0.68) * (0.8 + 0.3 * vnoise(vec3(a * 6.0, r * 20.0, seed * 5.0)));
                s.albedo = mix(mix(outerAlb, ringAlb, inRing), vec3(0.10, 0.10, 0.11), inDent);
                s.metal = mix(mix(k == 3 ? bare * 0.6 * inChip : 0.4, 0.65, inRing), 0.5, inDent);
                s.rough = mix(mix(0.65, 0.3 + 0.2 * t, inRing), 0.5, inDent);
                s.n = normalize(mix(N, pitNormal(N, T, B, dir, mix(0.6 * (1.0 - t), 0.9 * r / dent, inDent)), inRing));
                s.ao = mix(1.0, 0.6, inDent);
                alpha = mix(outerA, 1.0, inRing);
            }
            else if (k == 4)
            {
                // Thin sheet: punched clean through, the torn edge bent inwards in petals.
                float hole = 0.24 + 0.05 * ringNoise(a, seed, 5.0);
                float tears = abs(sin(a * 2.5 + seed * 6.0));
                float petals = hole + 0.1 + 0.1 * tears;
                float inHole = 1.0 - smoothstep(hole - aa, hole + aa, r);
                float inPetal = 1.0 - smoothstep(petals - aa, petals + aa, r);
                float t = clamp((r - hole) / (petals - hole), 0.0, 1.0);
                float to = clamp((r - petals) / max(1.0 - petals, 0.05), 0.0, 1.0);
                // Round the petals the paint has cracked off the dented sheet (primer and bare steel showing).
                float chip = petals + 0.12 + 0.18 * ringNoise(a, seed + 5.0, 3.5) + 0.08 * vnoise(vec3(q * 8.0, seed * 3.0));
                float inChip = 1.0 - smoothstep(chip - aa, chip + aa, r);
                float bare = smoothstep(0.5, 0.6, vnoise(vec3(q * 7.0, seed * 3.0)));
                vec3 chipped = mix(vec3(0.22, 0.11, 0.07), vec3(0.36, 0.35, 0.34), bare);
                vec3 petal = mix(vec3(0.64, 0.64, 0.66), vec3(0.01), (1.0 - smoothstep(0.0, 0.1, tears)) * (1.0 - t));
                s.albedo = mix(mix(mix(vec3(0.08), chipped, inChip), petal, inPetal), vec3(0.004), inHole);
                s.metal = mix(mix(0.3 * bare * inChip, 0.65, inPetal), 0.0, inHole);
                s.rough = mix(0.6, 0.35, inPetal);
                s.n = pitNormal(N, T, B, dir, mix(0.35 * (1.0 - to), 1.6 * (1.0 - t), inPetal) * (1.0 - inHole));
                s.ao = mix(1.0, 0.0, inHole);
                alpha = mix(mix(0.2 * (1.0 - to), 1.0, inChip), 1.0, inPetal);
            }
            else if (k == 5)
            {
                // Wood: a dark hole and raw, light wood torn out along the grain (the quad's x axis).
                float hole = 0.19 + 0.04 * ringNoise(a, seed, 3.0);
                float split = 0.55 + 0.35 * vnoise(vec3(q.x * 1.5, q.y * 9.0, seed * 7.0));
                float fiber = vnoise(vec3(q.x * 3.0, q.y * 30.0, seed * 5.0));
                float g = length(vec2(q.x * 0.7, q.y * 1.9));
                float inHole = 1.0 - smoothstep(hole - aa, hole + aa, r);
                float inTorn = 1.0 - smoothstep(split - aa, split + aa, g);
                s.albedo = mix(mix(vec3(0.50, 0.38, 0.25), vec3(0.72, 0.58, 0.40), fiber), vec3(0.02, 0.015, 0.01), inHole);
                s.n = normalize(pitNormal(N, T, B, dir, 0.5) + B * (fiber - 0.5) * 0.6);
                s.ao = mix(1.0, 0.2, inHole);
                alpha = max(inTorn, inHole);
            }
            else if (k == 6 || k == 14)
            {
                // Glass: a small hole, a ring of crushed white glass, radial cracks and a broken concentric one.
                float hole = 0.08 + 0.02 * ringNoise(a, seed, 3.0);
                float crush = 0.22 + 0.06 * ringNoise(a, seed + 1.0, 4.0);
                const float spokes = 9.0;
                float sector = a / 6.2831853 * spokes + seed * 3.0;
                float si = floor(sector);
                float jitter = hash13(vec3(cell, si));
                float line = abs(fract(sector) - 0.5 - (jitter - 0.5) * 0.6 + 0.08 * (vnoise(vec3(r * 12.0, si, seed)) - 0.5));
                float d = line * r * 6.2831853 / spokes;
                float radial = (1.0 - smoothstep(0.004, 0.004 + aa * 1.5, d)) * (1.0 - smoothstep(0.5 + 0.4 * jitter, 0.55 + 0.4 * jitter, r));
                float ringR = 0.42 + 0.06 * ringNoise(a, seed + 3.0, 2.0);
                float conc = (1.0 - smoothstep(0.004, 0.004 + aa * 1.5, abs(r - ringR))) * smoothstep(0.45, 0.55, ringNoise(a, seed + 7.0, 3.0));
                float inHole = 1.0 - smoothstep(hole - aa, hole + aa, r);
                float inCrush = 1.0 - smoothstep(crush - aa, crush + aa, r);
                // (On a bright, backlit pane the cracks and the crushed ring show dark against the light.)
                vec3 crack = k == 14 ? vec3(0.04, 0.045, 0.05) : vec3(0.85, 0.88, 0.9);
                vec3 crushed = k == 14 ? vec3(0.16, 0.17, 0.18) : vec3(0.75, 0.78, 0.8);
                s.albedo = mix(mix(crack, crushed, inCrush), vec3(0.01), inHole);
                s.rough = mix(mix(0.3, 0.55, inCrush), 1.0, inHole);
                alpha = max(max(radial, conc) * 0.9, mix(0.0, 0.75 + 0.25 * vnoise(vec3(q * 20.0, seed)), inCrush));
                alpha = max(alpha, inHole);
            }
            else if (k == 7 || k == 8 || k == 9)
            {
                // Ground: a small pit (moist dark earth, or blue shadow in snow) and clods thrown round it.
                vec3 base = k == 7 ? vec3(0.42, 0.16, 0.06) : (k == 8 ? vec3(0.22, 0.21, 0.19) : vec3(0.62, 0.68, 0.76));
                float pit = 0.34 + 0.1 * ringNoise(a, seed, 2.5);
                float inPit = 1.0 - smoothstep(pit - aa, pit + aa, r);
                float t = clamp(r / pit, 0.0, 1.0);
                float to = clamp((r - pit) / (1.0 - pit), 0.0, 1.0);
                float clods = smoothstep(0.58, 0.66, vnoise(vec3(q * 6.0, seed * 5.0)));
                vec3 pitAlb = k == 9 ? mix(vec3(0.30, 0.36, 0.44), base, t) : base * mix(0.25, 0.6, t);
                s.albedo = mix(k == 9 ? vec3(0.92, 0.94, 0.97) : base * 0.55, pitAlb, inPit);
                s.n = normalize(mix(N, pitNormal(N, T, B, dir, 0.9 * t), inPit));
                s.rough = 1.0;
                alpha = mix((clods * 0.85 + 0.35) * (1.0 - to * to), 1.0, inPit);
            }
            else if (k == 10 || k == 11)
            {
                // Plastic or rubber (a hole with a whitened stress ring), cloth (a frayed hole).
                float hole = 0.22 + 0.06 * ringNoise(a, seed, 4.0);
                float inHole = 1.0 - smoothstep(hole - aa, hole + aa, r);
                float ring = 1.0 - smoothstep(hole + 0.12, hole + 0.18 + aa, r);
                vec3 ringAlb = k == 10 ? vec3(0.32, 0.32, 0.33) : vec3(0.04);
                float ringA = k == 10 ? 0.6 * ring * (1.0 - clamp((r - hole) / 0.18, 0.0, 1.0))
                                      : ring * smoothstep(0.4, 0.5, vnoise(vec3(a * 5.0, r * 30.0, seed * 3.0)));
                s.albedo = mix(ringAlb, vec3(0.008), inHole);
                s.rough = mix(0.7, 1.0, inHole);
                s.n = normalize(mix(N, pitNormal(N, T, B, dir, 0.4), ring));
                alpha = max(ringA, inHole);
            }
            else if (k == 15)
            {
                // Contact shadow under a character's body and feet (the shadow maps are static): a soft dark pool.
                float fall = 1.0 - smoothstep(0.0, 1.0, r);
                s.albedo = vec3(0.0);
                s.rough = 1.0;
                alpha = fall * fall * (0.6 + 0.4 * fall);
            }
            else if (k == 16 || k == 17)
            {
                // Blood: 16 a splash thrown onto a surface (irregular, pulled out along the quad's x axis, the way it was
                // flying, with satellite droplets beyond it); 17 a single drop (round, with a few tiny spatters).
                // Wet and glossy, darker where it's thickest.
                bool splash = k == 16;
                float edge = (splash ? 0.5 : 0.62) + (splash ? 0.26 : 0.1) * ringNoise(a, seed, splash ? 5.0 : 3.0)
                           + (splash ? 0.12 * ringNoise(a, seed + 2.3, 11.0) : 0.0);
                float body = 1.0 - smoothstep(edge - aa, edge + aa, r);
                float sat = 0.0;
                for (int i = 0; i < 7; i++)
                {
                    float fi = float(i);
                    float ang = hash13(vec3(cell, fi)) * 6.283;
                    float reach = (splash ? 0.62 : 0.72) + 0.32 * hash13(vec3(cell, fi + 3.0));
                    vec2 c = vec2(cos(ang), sin(ang)) * reach;
                    if (splash)
                        c.x = abs(c.x) * 1.1;       // flung forward
                    float rr = (splash ? 0.04 : 0.03) + 0.06 * hash13(vec3(cell, fi + 7.0));
                    sat = max(sat, 1.0 - smoothstep(rr - aa, rr + aa, length(q - c)));
                }
                float thick = smoothstep(edge, 0.0, r);
                s.albedo = mix(vec3(0.17, 0.006, 0.005), vec3(0.07, 0.0, 0.002), thick);
                s.rough = 0.18;
                s.metal = 0.0;
                alpha = max(body, sat * (splash ? 1.0 : 0.7)) * 0.96;
            }
            else
            {
                // Ricochet gouge (12 on steel, 13 on stone): starts thin where the bullet struck and widens along the
                // quad's x axis (the way it was travelling), then stops.
                float width = 0.55 * smoothstep(-1.0, 0.3, q.x) * (1.0 - smoothstep(0.75, 1.0, q.x));
                float inside = 1.0 - smoothstep(width * 0.75, width + aa, abs(q.y) + 0.06 * vnoise(vec3(q * 12.0, seed)));
                s.albedo = k == 12 ? vec3(0.6, 0.6, 0.62) : vec3(0.36, 0.35, 0.33);
                s.metal = k == 12 ? 1.0 : 0.0;
                s.rough = k == 12 ? 0.25 : 0.95;
                s.n = normalize(N - B * sign(q.y) * 0.5 * inside);
                alpha = inside;
            }

            if (alpha * opacity < 0.004)
                discard;
            float dist = length(p - uCameraPos);
            vec3 viewDir = (p - uCameraPos) / max(dist, 1e-4);
            int area = areaOf(p);
            vec3 color = litBy(s, p, -viewDir, N, area);
            color = mix(color, vec3(luma(color)), area == 1 && uGlowCount > 0 ? 0.0 : (area == 6 || area == 13 ? 0.04 : (area == 7 || area == 10 || area == 12 ? 0.06 : (area == 9 || area == 11 ? 0.08 : 0.12))));
            color = applyAir(color, viewDir, dist, area);
            return vec4(tonemap(color) + dither(), clamp(alpha * opacity, 0.0, 1.0));
        }

        // Particles (mode 6): kind 0 dust, 1 chip, 2 water droplet, 6 snow, 8 smoke screen are lit; 3 spark, 4 tracer, 5 muzzle flash
        // and 7 trail glow (their colour is linear HDR and they're drawn additively). Alpha comes in tangent.w like decals.
        vec4 shadeParticle()
        {
            float kindF = floor(vTangent.w);
            int k = int(kindF + 0.5);
            float alpha = clamp((vTangent.w - kindF - 0.05) / 0.9, 0.0, 1.0);
            vec2 q = vUV;
            vec3 p = vWorldPos;
            float dist = length(p - uCameraPos);
            vec3 viewDir = (p - uCameraPos) / max(dist, 1e-4);
            int area = areaOf(p);
            if ((k >= 3 && k <= 5) || k == 7)
            {
                float m;
                if (k == 5)
                {
                    float r = length(q);
                    float ang = atan(q.y, q.x);
                    float star = pow(abs(cos(ang * 2.5)), 8.0);
                    m = (1.0 - smoothstep(0.0, 0.4, r)) + 0.9 * (1.0 - smoothstep(0.0, 1.0, r / (0.3 + 0.7 * star)));
                }
                else
                {
                    float across = 1.0 - q.x * q.x;
                    m = across * across * (k == 3 ? 1.0 - 0.7 * q.y : k == 7 ? 1.0 : 1.0 - q.y);
                }
                float fog = area == 4 ? exp(-dist * 0.036) : (area == 0 ? exp(-dist * 0.02) : (area == 6 ? exp(-dist * 0.016) : 1.0));
                return vec4(tonemap(vTangent.rgb), clamp(m * alpha * fog, 0.0, 1.0));
            }

            // Kind 8, a smoke grenade's cloud: cool, so a thermal imager sees straight through it.
            if (k == 8 && uVision == 2)
                discard;
            float r = length(q);
            // A smoke screen's puffs are lit once each (uParticleBake: see the vertex shader), where their centres are:
            // hundreds of them overlap, each metres across, and lighting every pixel of every one took the frame rate
            // to single figures. Here either that light is worked out (1: p is the puff's centre, and this is its texel)
            // or it's what the puff is drawn with (2: it comes in vNormal, already as the screen shows it).
            if (k == 8 && uParticleBake == 2)
            {
                if (r >= 1.0)
                    discard;
                // (Two octaves of noise, scaled to fbm3's range: the third is lost among so many layers, and this runs for every pixel of each.)
                vec3 at = vec3(q * 1.2, 0.0) + p * 0.7 + vec3(0.0, uTime * 0.15, 0.0);
                float puff = (1.0 - smoothstep(0.0, 1.0, r)) * (0.65 + 0.35 * 1.1667 * (0.5 * vnoise(at) + 0.25 * vnoise(at * 2.03 + 1.7)));
                if (alpha * puff < 0.004)
                    discard;
                return vec4(vNormal + dither(), clamp(alpha * puff, 0.0, 1.0));
            }
            bool baking = k == 8 && uParticleBake == 1;
            float mask = baking ? 1.0
                       : k == 1 ? 1.0 - smoothstep(0.7, 1.0, max(abs(q.x), abs(q.y)))
                       : k == 2 ? 1.0 - smoothstep(0.5, 1.0, r)
                       : k == 8 ? (1.0 - smoothstep(0.0, 1.0, r)) * (0.65 + 0.35 * fbm3(vec3(q * 1.2, 0.0) + p * 0.7 + vec3(0.0, uTime * 0.15, 0.0)))
                       : (1.0 - smoothstep(0.15, 1.0, r)) * (0.5 + 0.5 * fbm3(vec3(q * 1.6, 0.0) + p * 2.0));
            if (!baking && alpha * mask < 0.004)
                discard;
            Surface s;
            s.albedo = toLinear(vTangent.rgb);
            s.mask = 0.0;
            s.ao = 1.0;
            s.metal = 0.0;
            s.rough = k == 2 ? 0.15 : 1.0;
            s.n = normalize(-viewDir + vec3(0.0, 0.9, 0.0));
            vec3 c = litBy(s, p, -viewDir, s.n, area);
            c = applyAir(c, viewDir, dist, area);
            if (baking)
                return vec4(tonemap(c), 1.0);
            return vec4(tonemap(c) + dither(), clamp(alpha * mask, 0.0, 1.0));
        }

        // ------------------------------------------------------------- scope vision (see Renderer.SetVision)
        bool gVisionDone = false;   // this fragment already is what the scope shows

        // An image intensifier: the picture's brightness lifted hard out of the dark and shown in phosphor green, with
        // the tube's grain and scintillation, and the brightest things burning out towards white.
        vec3 nightVisionView(vec3 c)
        {
            float frame = floor(uTime * 30.0);
            float g = 1.0 - exp(-1.8 * pow(clamp(luma(c), 0.0, 1.0), 0.6));
            g *= 0.93 + 0.14 * hash13(vec3(gl_FragCoord.xy, frame));
            g += (hash13(vec3(gl_FragCoord.yx, frame + 7.0)) - 0.5) * 0.035;
            g += step(0.9993, hash13(vec3(gl_FragCoord.xy, frame + 13.0))) * 0.6;
            g = clamp(g, 0.0, 1.0);
            vec3 green = mix(vec3(0.0, 0.02, 0.005), vec3(0.25, 1.0, 0.35), g);
            return mix(green, vec3(0.85, 1.0, 0.85), smoothstep(0.72, 0.86, g));
        }

        // A thermal imager's palette (ironbow): cold deep blue, through purple and red, to pale yellow-hot.
        vec3 thermalColor(float heat)
        {
            float t = clamp(heat + (hash13(vec3(gl_FragCoord.xy, floor(uTime * 30.0))) - 0.5) * 0.012, 0.0, 1.0) * 4.0;
            if (t < 1.0) return mix(vec3(0.01, 0.0, 0.07), vec3(0.20, 0.02, 0.45), t);
            if (t < 2.0) return mix(vec3(0.20, 0.02, 0.45), vec3(0.78, 0.12, 0.38), t - 1.0);
            if (t < 3.0) return mix(vec3(0.78, 0.12, 0.38), vec3(0.98, 0.55, 0.05), t - 2.0);
            return mix(vec3(0.98, 0.55, 0.05), vec3(1.0, 0.97, 0.75), t - 3.0);
        }

        // How hot a surface looks: no light comes into it, so it reads the same by night. The world is cold, its shapes
        // showing faintly (what faces the sky differs from what doesn't, bare metal reads colder), flattening with distance;
        // people stand out, their bare skin hottest, clothes less so, and hard kit (boots, helmets) less again.
        float surfaceHeat(Surface s, vec3 viewDir, float dist)
        {
            float form = 0.5 + 0.5 * dot(s.n, normalize(vec3(0.3, 0.8, 0.2)));
            float t = 0.07 + 0.19 * sqrt(clamp(luma(s.albedo), 0.0, 1.0)) + 0.11 * form - 0.04 * s.metal;
            if (uMaterial == 23)
                t *= 0.6;   // snow
            t = mix(0.12, t, exp(-dist * 0.004));
            if (uSkin == 2)
            {
                float body = uWoundStyle == 0 ? 0.95 : (uWoundStyle == 1 ? 0.76 : 0.52);
                body *= mix(0.8, 1.0, clamp(dot(s.n, -viewDir), 0.0, 1.0));
                t = mix(t, mix(0.2, body, exp(-dist * 0.0012)), uBodyHeat);
            }
            return t;
        }

        void shade()
        {
            if (uShadowPass == 1)
            {
                if (uCutout == 1 && texture(uAlbedo, vUV).a < 0.5)
                    discard;   // leaves cast leafy shadows, not square ones
                fragColor = vec4(1.0);
                return;
            }

            if (uMode == 5)
            {
                fragColor = shadeDecal();
                return;
            }

            if (uMode == 6)
            {
                fragColor = shadeParticle();
                return;
            }

            if (uMode == 9)
            {
                // A prism sight's eyepiece (see Renderer.DrawScopeRing), drawn over the whole screen: black from the
                // circle of radius uColor.x (in half screen heights, round the centre) out to uColor.y, fading out over
                // uColor.z past that; just inside the circle the picture darkens a little towards its edge.
                vec2 p = (gl_FragCoord.xy - 0.5 * uScreenSize) / (0.5 * uScreenSize.y);
                float r = length(p);
                float aa = 1.5 / (0.5 * uScreenSize.y);
                float ring = smoothstep(uColor.x - aa, uColor.x + aa, r) * (1.0 - smoothstep(uColor.y, uColor.y + uColor.z, r));
                float edge = r < uColor.x ? 0.5 * smoothstep(uColor.x * 0.82, uColor.x, r) : 0.0;
                fragColor = vec4(0.0, 0.0, 0.0, max(ring, edge));
                return;
            }

            if (uMode == 2)
            {
                fragColor = vec4(uColor, 1.0);
                return;
            }

            if (uMode == 10)
            {
                // Clear glass: you see through it, less so at a glancing angle, with a faint film of dirt on it.
                float cosine = abs(dot(normalize(vNormal), normalize(uCameraPos - vWorldPos)));
                float fresnel = 0.04 + 0.96 * pow(1.0 - cosine, 5.0);
                fragColor = vec4(airOverDisplay(uColor, vWorldPos), clamp(0.13 + 0.22 * fbm3(vWorldPos * 3.0) + fresnel * 0.7, 0.0, 0.85));
                return;
            }

            if (uMode == 11)
            {
                // A helicopter's blades as a blur: the tangent's w is its opacity, the uv's x how much is the blades' lighter tip.
                vec3 shown = mix(uColor, vec3(0.5, 0.5, 0.48), vUV.x);
                fragColor = vec4(airOverDisplay(shown, vWorldPos), clamp(vTangent.w, 0.0, 1.0));
                return;
            }

            if (uMode == 4)
            {
                vec2 q = (uPortalWorldToLocal * vec4(vWorldPos, 1.0)).xy;
                if (length(q) > uPortalRadius)
                    discard;
                if (uVision == 2)
                {
                    fragColor = vec4(thermalColor(0.14), 1.0);   // (the liquid, as in mode 3: cool, not see-through)
                    gVisionDone = true;
                    return;
                }
                fragColor = vec4(airOverDisplay(uColor, vWorldPos), 1.0);
                return;
            }

            if (uMode == 12)
            {
                // A picture laid over the box's front face by its own coordinates, its colours untouched (the texture is
                // uploaded as plain RGBA, and the window isn't an sRGB framebuffer, so its pixels arrive as they are in the file).
                vec2 uv = (uPortalWorldToLocal * vec4(vWorldPos, 1.0)).xy + 0.5;
                fragColor = texture(uPortalTexture, clamp(uv, vec2(0.0), vec2(1.0)));
                return;
            }

            if (uMode == 7)
            {
                // A monitor showing a camera's feed (already tone-mapped): the texture laid over the box's front face
                // by its own coordinates, with the look of cheap CCTV: washed-out colour, scanlines, grain, a slow
                // rolling bar and darker corners.
                vec2 uv = (uPortalWorldToLocal * vec4(vWorldPos, 1.0)).xy + 0.5;
                vec3 c = texture(uPortalTexture, clamp(uv, vec2(0.001), vec2(0.999))).rgb;
                float luma = dot(c, vec3(0.299, 0.587, 0.114));
                c = mix(vec3(luma), c, 0.4) * vec3(0.93, 1.0, 0.96);
                c *= 0.86 + 0.14 * sin(uv.y * 900.0);
                c += (hash13(vec3(floor(uv * vec2(320.0, 180.0)), floor(uTime * 15.0))) - 0.5) * 0.07;
                c *= 1.0 - 0.07 * smoothstep(0.0, 0.04, fract(uv.y * 0.6 - uTime * 0.08)) * smoothstep(0.12, 0.04, fract(uv.y * 0.6 - uTime * 0.08));
                vec2 v = uv - 0.5;
                c *= 1.0 - 0.9 * dot(v, v);
                fragColor = vec4(airOverDisplay(clamp(c, 0.0, 1.0) * uColor, vWorldPos), 1.0);
                return;
            }

            if (uMode == 8)
            {
                // A one-way mirror: from the front (the box's +z face), the reflected view rendered this frame (already
                // tone-mapped), slightly darkened and cooled by the silvering; from behind nothing, so it's clear glass.
                if ((uPortalWorldToLocal * vec4(uCameraPos, 1.0)).z < 0.5)
                    discard;
                if (uVision != 0)
                {
                    // (The reflection was drawn through the scope's vision already.)
                    fragColor = vec4(texture(uPortalTexture, gl_FragCoord.xy / uScreenSize).rgb, 1.0);
                    gVisionDone = true;
                    return;
                }
                vec3 c = texture(uPortalTexture, gl_FragCoord.xy / uScreenSize).rgb * vec3(0.84, 0.87, 0.88);
                c += (fbm3(vWorldPos * 3.0) - 0.5) * 0.025;   // faint smears on the glass
                fragColor = vec4(airOverDisplay(clamp(c * uColor, 0.0, 1.0), vWorldPos), 1.0);
                return;
            }

            if (uMode == 3)
            {
                // Portal surface: the off-screen view from the virtual camera (already tone-mapped), seen through
                // a rippling water surface.
                vec2 p = (uPortalWorldToLocal * vec4(vWorldPos, 1.0)).xy;
                if (length(p) > uPortalRadius)
                    discard;
                const float e = 0.004;
                float h = waterHeight(p);
                vec2 slope = vec2(waterHeight(p + vec2(e, 0.0)) - h, waterHeight(p + vec2(0.0, e)) - h) / e;

                if (uVision == 2)
                {
                    // A thermal imager can't see through the liquid: it reads its surface, cool and even, the ripples
                    // and the scum on it showing faintly, a little warmer round the rim.
                    float rimHeat = smoothstep(uPortalRadius - 0.45, uPortalRadius, length(p));
                    float scum = smoothstep(0.35, 0.75, fbm(vec3(p * 2.5, uTime * 0.05)));
                    fragColor = vec4(thermalColor(0.13 + clamp(length(slope) * 0.04, 0.0, 0.05) + 0.03 * scum + 0.04 * rimHeat), 1.0);
                    gVisionDone = true;
                    return;
                }

                vec2 uv = gl_FragCoord.xy / uScreenSize + slope * 0.025;
                vec3 view = texture(uPortalTexture, clamp(uv, vec2(0.001), vec2(0.999))).rgb;
                if (uVision != 0)
                {
                    // (The view beyond was drawn through the scope's vision already; only its ripple shows.)
                    fragColor = vec4(view, 1.0);
                    gVisionDone = true;
                    return;
                }

                vec3 n = normalize(vec3(-slope * 0.5, 1.0));
                float spec = pow(max(dot(n, normalize(vec3(0.45, 0.6, 0.65))), 0.0), 40.0);
                float crest = clamp(length(slope) * 0.25, 0.0, 1.0);
                vec3 water = view * vec3(0.90, 0.96, 1.0) + vec3(0.0, 0.008, 0.015);
                water += vec3(0.9, 0.95, 1.0) * (spec * 0.5 + crest * 0.08);

                float rim = smoothstep(uPortalRadius - 0.45, uPortalRadius, length(p));
                float film = (0.25 + 0.75 * rim) * smoothstep(0.35, 0.75, fbm(vec3(p * 2.5, uTime * 0.05)));
                water = mix(water, vec3(0.20, 0.17, 0.12), film * 0.45);
                vec2 drift = p * 9.0 + vec2(uTime * 0.07, -uTime * 0.05);
                float speck = step(0.93, hash13(vec3(floor(drift), 5.0))) * (1.0 - smoothstep(0.08, 0.2, length(fract(drift) - 0.5)));
                water = mix(water, vec3(0.08, 0.07, 0.05), speck * 0.7);

                fragColor = vec4(airOverDisplay(mix(water, uColor * 0.5, 0.035), vWorldPos), 1.0);
                return;
            }

            vec3 n = normalize(vNormal);
            vec3 p = vWorldPos;

            // Leaf cards: cut out by the texture's alpha (sharpened to about a pixel wide, so it antialiases with
            // alpha-to-coverage on the multisampled screen), and lit from whichever side faces the camera.
            float coverage = 1.0;
            if (uCutout == 1)
            {
                float a = texture(uAlbedo, vUV).a;
                coverage = clamp((a - 0.5) / max(fwidth(a), 1e-4) + 0.5, 0.0, 1.0);
                if (coverage < (uA2C == 1 ? 0.02 : 0.5))
                    discard;
                // Turn the normal towards the camera only when it faces away: flat cards light from the side you
                // see, while bushes and tree crowns keep their soft, rounded (radial) normals on both sides.
                if (dot(n, uCameraPos - p) < 0.0)
                    n = -n;
            }
            vec3 pt = (uTextureSpace * vec4(vTexPos, 1.0)).xyz; // where textures are mapped (camera-relative on the view model)
            vec3 nt = normalize(mat3(uTextureSpace) * n);
            // (A sky box can reach over an area's edge, so the sky takes the camera's area, not its own.)
            int area = uMaterial == 10 ? areaOf(uCameraPos) : areaOf(p);
            if (uMaterial == 10 && area == 0)
                area = 8;   // the sky seen out of the warehouse's door is the factory grounds'
            if (uMaterial == 10 && area == 10)
                area = 9;   // the sky seen out of a prison window is the yard's
            if (uMaterial == 10 && area == 12)
                area = 11;
            bool outback = area == 2 || area == 8 || area == 13;
            bool outdoors = outdoorArea(area);
            float dist = length(p - uCameraPos);
            vec3 viewDir = (p - uCameraPos) / max(dist, 1e-4);

            if (uVision == 2 && (uMaterial == 10 || uMaterial == 6 || uMaterial == 19 || uMaterial == 24))
            {
                // In thermal: the sky is coldest of all, the sea and window glass cold, lamps warm.
                float heat = uMaterial == 10 ? 0.04 + 0.06 * (1.0 - clamp(viewDir.y, 0.0, 1.0))
                           : uMaterial == 24 ? 0.09 + 0.03 * vnoise(p * 0.4)
                           : uMaterial == 6 ? 0.2 : 0.3 + 0.45 * luma(uColor);
                fragColor = vec4(thermalColor(heat), 1.0);
                gVisionDone = true;
                return;
            }

            if (uMaterial == 10)
            {
                // Sky: the area's HDR cubemap, plus the sun's disc and glow (the cubemap has no disc of its own).
                vec3 c = skyRadiance(viewDir, 0.0, area) * (area == 1 ? uSkyDisplayYard : 1.0);
                if (nightArea(area))
                    c = nightSkyView(c, viewDir, area);
                vec3 sunDir = area == 13 ? uSunRio : outback ? uSunOutback : (area == 5 ? uSunShip : (area == 6 ? uSunJungle : (area == 9 || area == 11 ? uSunPrison : uSunYard)));
                vec3 sunE = sunColorOf(area) * (area == 5 && nightArea(5) ? SHIP_NIGHT_OUTSIDE : 1.0);   // (the moon's glow through the ship's overcast)
                float cosA = dot(viewDir, sunDir);
                if (outback)
                {
                    float disc = smoothstep(0.999986, 0.999991, cosA);   // ~0.27 degree radius
                    c += sunE * (disc * 3000.0 + pow(max(cosA, 0.0), 2000.0) * 6.0 + pow(max(cosA, 0.0), 60.0) * 0.08);
                }
                else if (area == 4)
                {
                    c = arcticAir(c, uCameraPos, viewDir, 1.0e4);                                       // blowing snow
                }
                else if (area == 6)
                {
                    c += sunE * (pow(max(cosA, 0.0), 900.0) * 6.0 + pow(max(cosA, 0.0), 30.0) * 0.12);   // the sun in the haze
                    c = mix(c, jungleHaze() * 1.6, 0.35 * (1.0 - smoothstep(0.0, 0.35, viewDir.y)));       // humid haze at the horizon
                }
                else if (area == 5 && viewDir.y < 0.0)
                {
                    // The sea, on out past the edge of the sea box to the horizon.
                    float t = uCameraPos.y > SEA_LEVEL ? (SEA_LEVEL - uCameraPos.y) / viewDir.y : 0.0;
                    c = uCameraPos.y > SEA_LEVEL ? oceanColor(uCameraPos + viewDir * t, viewDir, t, area) : vec3(0.004, 0.012, 0.016);
                }
                else if (area == 1 && viewDir.y < 0.0 && uCameraPos.y > YARD_SEA_LEVEL
                         && uCameraPos.x + viewDir.x * (YARD_SEA_LEVEL - uCameraPos.y) / viewDir.y < YARD_QUAY_X)
                {
                    // The sea off the Antwerp yard's quay, west of its edge, out to the horizon.
                    float t = (YARD_SEA_LEVEL - uCameraPos.y) / viewDir.y;
                    c = oceanColor(uCameraPos + viewDir * t, viewDir, t, area);
                }
                else
                {
                    c += sunE * (pow(max(cosA, 0.0), 300.0) * 1.5 + pow(max(cosA, 0.0), 12.0) * 0.06); // glow through cloud
                    if (area == 1)
                        c += glowScatter(uCameraPos, viewDir, 2000.0);                                 // lamps in the haze
                }
                fragColor = vec4(tonemap(c) + dither(), 1.0);
                return;
            }

            if (uMaterial == 6)
            {
                // Emissive windows, the doorway, lamp bulbs: display-space colours, as before.
                vec3 color = windowGlass(p, n, uColor);
                float airLevel = nightArea(area) ? 0.12 : 1.0;   // (the display-space colours of the air below, by night)
                if (outback)
                    fragColor = vec4(mix(color, vec3(0.66, 0.74, 0.84) * airLevel, (1.0 - exp(-dist * 0.0035)) * 0.85), 1.0);
                else if (area == 4)
                    fragColor = vec4(mix(color, tonemap(uWhiteout), 1.0 - exp(-dist * 0.036)), 1.0);
                else if (area == 6)
                    fragColor = vec4(mix(color, tonemap(jungleHaze()), (1.0 - exp(-dist * 0.016)) * 0.85), 1.0);
                else if (area == 7 || area == 10 || area == 12)
                    fragColor = vec4(color, 1.0);   // the police station's and the prison's windows: clean air, no haze
                else if (outdoors)
                    fragColor = vec4(mix(color, vec3(0.74, 0.76, 0.79), (1.0 - exp(-dist * 0.012)) * 0.6), 1.0);
                else
                    fragColor = vec4(mix(color, uFogColor * airLevel, (1.0 - exp(-dist * 0.035)) * 0.75), 1.0);
                return;
            }

            if (uMaterial == 19)
            {
                fragColor = vec4(uColor * (area == 12 ? max(flatsLampFlicker(p.y), 0.05) : 1.0), 1.0); // glowing bulbs and lights
                return;
            }

            if (uMaterial == 24)
            {
                fragColor = vec4(tonemap(oceanColor(p, viewDir, dist, area)) + dither(), 1.0);
                return;
            }

            Surface s;
            if (uTextured == 1)
                s = sampleSurface(p, pt, n, nt);
            else
            {
                s.albedo = toLinear(uColor);
                s.mask = 0.0;
                s.n = n;
                s.ao = 1.0;
                s.rough = uRoughMetal.x;
                s.metal = uRoughMetal.y;
            }
            addWear(s, p, pt, n);
            if (uSkin == 2 && uWoundCount > 0)
                applyWounds(s);
            if (uMaterial == 11 && area == 2 && n.y > 0.5 && p.y < 0.05)
                paintRoad(s, p, n);
            if (area == 4 && uMaterial != 23 && uViewModel == 0 && uSkin != 2 && !sunShutOut(p))
            {
                // Everything in the Arctic wears snow on its upward faces (but not inside the hollow container).
                float cover = smoothstep(0.55, 0.85, n.y + (vnoise(p * 2.3) - 0.5) * 0.5) * (uMaterial == 25 ? 0.4 : 1.0);
                s.albedo = mix(s.albedo, vec3(0.80, 0.84, 0.88), cover);
                s.rough = mix(s.rough, 0.75, cover);
                s.metal *= 1.0 - cover;
            }
            if (area == 6 && uViewModel == 0 && uSkin != 2)
                jungleDamp(s, p, n);

            if (uVision == 2)
            {
                fragColor = vec4(thermalColor(surfaceHeat(s, viewDir, dist)), coverage);   // (no lighting to work out)
                gVisionDone = true;
                return;
            }

            vec3 V = -viewDir;
            if (uStudio == 1)
            {
                fragColor = vec4(tonemap(studioLight(s, V)) + dither(), coverage);
                return;
            }
            vec3 color = outdoors ? outdoorLight(s, p, V, n, area)
                       : (area == 3 ? bunkerLight(s, p, V) : (area == 7 || area == 10 || area == 12 ? officeLight(s, p, V, n) : indoorLight(s, p, V)));
            color += flashLight(s, p, V);
            if (uFlareCount > 0)
                color += flareLights(s, p, V, n, outdoors);
            if (uVision == 1)
                color += s.albedo * max(dot(s.n, V), 0.0) * 0.35 / (1.0 + dist * dist * 0.004);   // the scope's infrared illuminator, for where there's no light at all
            if (uCutout == 1 && (area == 1 || area == 2 || area == 8 || area == 13))
            {
                // Sunlight through the leaves, seen from the shaded side.
                vec3 sunDir = area == 13 ? uSunRio : outback ? uSunOutback : uSunYard;
                vec3 sunE = sunColorOf(area);
                float shadow = area == 8 ? (uFactoryShadowReady == 1 ? shadowAt(uShadowFactory, uShadowFactoryVP, p, n, sunDir) : 1.0)
                             : area == 13 ? (uRioShadowReady == 1 ? shadowAt(uShadowRio, uShadowRioVP, p, n, sunDir) : 1.0)
                             : outback ? shadowAt(uShadowOutback, uShadowOutbackVP, p, n, sunDir) : shadowAt(uShadowYard, uShadowYardVP, p, n, sunDir);
                color += s.albedo * sunE * max(dot(-s.n, sunDir), 0.0) * (0.45 / PI) * shadow * vec3(0.9, 1.0, 0.6);
            }

            // Night: puddles mirror the lamps' halos.
            if (area == 1 && uGlowCount > 0 && uMaterial == 9 && s.rough < 0.2)
            {
                vec3 R = reflect(viewDir, s.n);
                float NoV = max(dot(s.n, -viewDir), 1e-4);
                color += glowReflection(p, n, R) * envBRDF(vec3(0.02), s.rough, NoV) * (1.0 - smoothstep(0.04, 0.2, s.rough));
            }

            // A touch of desaturation for the grimy, washed-out look (by day; sodium nights stay saturated).
            color = mix(color, vec3(luma(color)), area == 1 && uGlowCount > 0 ? 0.0 : (area == 6 || area == 13 ? 0.04 : (area == 7 || area == 10 || area == 12 ? 0.06 : (area == 9 || area == 11 ? 0.08 : 0.12))));

            // Haze: dusty and thick indoors, light outdoors (coloured by the sky towards the horizon), a whiteout in
            // the Arctic.
            color = applyAir(color, viewDir, dist, area);

            fragColor = vec4(tonemap(color) + dither(), coverage);
        }

        void main()
        {
            shade();
            // Whatever hasn't been worked out for the scope's vision already (particles, bullet holes, glass, flat colours,
            // monitors...) takes it from how bright it would have looked.
            if (uVision != 0 && uShadowPass == 0 && !gVisionDone)
            {
                float bright = clamp(luma(fragColor.rgb), 0.0, 1.0);
                fragColor.rgb = uVision == 1 ? nightVisionView(fragColor.rgb) : thermalColor(0.1 + 0.75 * bright * bright);
            }
        }
        """;

    /// <summary>Colour of the dusty haze indoors (also the clear colour).</summary>
    public static readonly float3 SkyColor = new(0.21f, 0.20f, 0.18f);

    public const int MaxLights = 8;    // must match MAX_LIGHTS in the fragment shader
    private const int FlareShadowUnit = 19;
    private readonly int _uFlarePos, _uFlareColor, _uFlareCount;
    public const int MaxRipples = 16;  // must match MAX_RIPPLES in the fragment shader

    // How bright a white surface in full sun ends up in each outdoor area (linear, before tone mapping), and how much
    // of the sun gets through the overcast over the yard.
    public static float YardWhiteLevel = 1.05f;
    public static float OutbackWhiteLevel = 1.45f;
    public static float YardSunFactor = 0.32f;

    /// <summary>At night (a night sky for the yard): how bright a white surface lit only by the sky ends up.</summary>
    public static float YardNightAmbient = 0.004f;

    /// <summary>How strongly the night air scatters the lamps' light towards the eye (their glow and halos).</summary>
    public static float NightGlowScatter = 0.00013f;

    /// <summary>How bright the night sky itself looks, relative to the ambient light it gives.</summary>
    public static float NightSkyDisplay = 0.35f;

    /// <summary>How strong the halo round each lamp in view is.</summary>
    public static float NightGlare = 1.4f;

    /// <summary>The tints the texture sets were made for (other tints scale the bare material's brightness).</summary>
    private static readonly Dictionary<Material, float3> RefTints = new()
    {
        [Material.Concrete] = new(0.62f, 0.60f, 0.55f),
        [Material.Floor] = new(0.40f, 0.37f, 0.33f),
        [Material.RustyMetal] = new(0.30f, 0.27f, 0.25f),
        [Material.Steel] = new(0.24f, 0.24f, 0.25f),
        [Material.Wood] = new(0.46f, 0.34f, 0.22f),
        [Material.PaintedMetal] = new(0.60f, 0.60f, 0.58f),
        [Material.Corrugated] = new(0.66f, 0.66f, 0.63f),
        [Material.Galvanized] = new(0.55f, 0.55f, 0.53f),
        [Material.Gravel] = new(0.40f, 0.39f, 0.37f),
        [Material.RedDirt] = new(0.72f, 0.30f, 0.12f),
        [Material.DirtRoad] = new(0.92f, 0.52f, 0.27f),
        [Material.Bush] = new(0.50f, 0.56f, 0.36f),
        [Material.Bark] = new(0.62f, 0.48f, 0.38f),
        [Material.Gunmetal] = new(0.22f, 0.22f, 0.23f),
        [Material.Polymer] = new(0.19f, 0.19f, 0.20f),
        [Material.Skin] = new(0.66f, 0.47f, 0.37f),
        [Material.Cloth] = new(0.60f, 0.60f, 0.60f),
        [Material.Snow] = new(0.86f, 0.89f, 0.93f),
        [Material.Rock] = new(0.30f, 0.30f, 0.31f),
        [Material.MarinePaint] = new(0.84f, 0.84f, 0.82f),
        [Material.Fleece] = new(0.60f, 0.60f, 0.60f),
        [Material.Denim] = new(0.60f, 0.60f, 0.60f),
        [Material.Canvas] = new(0.60f, 0.60f, 0.60f),
        [Material.Leather] = new(0.60f, 0.60f, 0.60f),
        [Material.Knit] = new(0.60f, 0.60f, 0.60f),
        [Material.Hair] = new(0.60f, 0.60f, 0.60f),
    };

    public static float3 RefTint(Material material) => RefTints.TryGetValue(material, out var t) ? t : new float3(0.6f);

    private readonly GlProgram _program;
    private readonly Mesh _cube;
    private readonly Dictionary<string, int> _locations = [];
    private TextureLibrary? _textures;
    private Material _boundMaterial = (Material)(-1);
    private TextureSet? _boundSet;
    private bool _boundTextured;

    private readonly int _uModel, _uViewProjection, _uColor, _uMode, _uMaterial, _uTextureSpace, _uCameraPos, _uClipPlane;
    private readonly int _uTextured, _uUseUV, _uTile, _uRefTint, _uTintMode, _uRoughMetal;
    private readonly int _uCutout, _uA2C, _uProjectFrom, _uViewOrigin, _uParticleBake;

    /// <summary>The side of the texture a smoke screen's puffs are lit into, a texel each (the shader's SMOKE_LIGHT_SIZE): room for its square.</summary>
    public const int SmokeLightSize = 32;
    private const int SmokeLightUnit = 21;
    private readonly RenderTarget _smokeLight = new();
    private readonly bool _smokeLightReady;
    // The view being drawn (BeginView), to come back to after lighting the smoke.
    private RenderTarget? _viewTarget;
    private int2 _viewSize;
    private bool _viewScissored;
    private readonly int _uSkin, _uSkinModel, _uBones, _uWoundStyle, _uWoundCount, _uWounds, _uWoundInfo;

    /// <summary>Wounds a skinned character can show at once (must match uWounds in the fragment shader).</summary>
    public const int MaxWounds = 12;

    /// <summary>Joints a skinned model may have (must match uBones in the vertex shader).</summary>
    public const int MaxBones = 60;

    /// <summary>The camera position of the view being drawn (for choosing levels of detail).</summary>
    public float3 CameraPosition { get; private set; }
    private readonly int _uShadowPass, _uScreenSize, _uPortalWorldToLocal, _uPortalRadius, _uRipples, _uRippleCount, _uTime, _uLights, _uLightCount;

    public Renderer()
    {
        using var vertexShader = CreateShader(GlShaderType.Vertex).SetSource(VertexSource).Compile();
        using var fragmentShader = CreateShader(GlShaderType.Fragment).SetSource(FragmentSource).Compile();
        _program = CreateProgram().Attach(vertexShader, fragmentShader).Link();

        _uModel = Loc("uModel");
        _uViewProjection = Loc("uViewProjection");
        _uColor = Loc("uColor");
        _uMode = Loc("uMode");
        _uMaterial = Loc("uMaterial");
        _uTextureSpace = Loc("uTextureSpace");
        _uCameraPos = Loc("uCameraPos");
        _uClipPlane = Loc("uClipPlane");
        _uTextured = Loc("uTextured");
        _uUseUV = Loc("uUseUV");
        _uTile = Loc("uTile");
        _uRefTint = Loc("uRefTint");
        _uTintMode = Loc("uTintMode");
        _uRoughMetal = Loc("uRoughMetal");
        _uShadowPass = Loc("uShadowPass");
        _uScreenSize = Loc("uScreenSize");
        _uPortalWorldToLocal = Loc("uPortalWorldToLocal");
        _uPortalRadius = Loc("uPortalRadius");
        _uRipples = Loc("uRipples[0]");
        _uRippleCount = Loc("uRippleCount");
        _uTime = Loc("uTime");
        _uLights = Loc("uLights[0]");
        _uLightCount = Loc("uLightCount");
        _uCutout = Loc("uCutout");
        _uSkin = Loc("uSkin");
        _uSkinModel = Loc("uSkinModel");
        _uBones = Loc("uBones[0]");
        _uWoundStyle = Loc("uWoundStyle");
        _uWoundCount = Loc("uWoundCount");
        _uWounds = Loc("uWounds[0]");
        _uWoundInfo = Loc("uWoundInfo[0]");
        _uA2C = Loc("uA2C");
        _uProjectFrom = Loc("uProjectFrom");
        _uViewOrigin = Loc("uViewOrigin");

        _cube = new MeshBuilder().Box(float3.Zero, float3.One).Build();

        _program.Uniform(Loc("uFogColor"), SkyColor);
        _program.Uniform(Loc("uPortalTexture"), 0); // texture unit 0
        _program.Uniform(Loc("uShadowYard"), 1);
        _program.Uniform(Loc("uShadowOutback"), 2);
        _program.Uniform(Loc("uAlbedo"), 3);
        _program.Uniform(Loc("uNormalMap"), 4);
        _program.Uniform(Loc("uOrm"), 5);
        _program.Uniform(Loc("uRoadAlbedo"), 6);
        _program.Uniform(Loc("uRoadNormal"), 7);
        _program.Uniform(Loc("uRoadOrm"), 8);
        _program.Uniform(Loc("uSkyYard"), 9);
        _program.Uniform(Loc("uSkyOutback"), 10);
        _program.Uniform(Loc("uFloodShadow"), 11);
        _program.Uniform(Loc("uSkyShip"), 12);
        _program.Uniform(Loc("uShadowShip"), 13);
        _program.Uniform(Loc("uShadowJungle"), 14);
        _program.Uniform(Loc("uOfficeOcc"), 15);
        // Unit 16 (the factory grounds' sun shadow) exists on most GPUs; where it doesn't, it's never bound.
        Span<int> units = stackalloc int[1];
        GetIntegerv(GL_MAX_TEXTURE_IMAGE_UNITS, units);
        GetError();
        MaxTextureUnits = units[0];
        if (MaxTextureUnits > 16)
            _program.Uniform(Loc("uShadowFactory"), 16);
        if (MaxTextureUnits > 17)
            _program.Uniform(Loc("uShadowPrison"), 17);
            _program.Uniform(Loc("uShadowFlats"), 18);
        // Unit 20: Rio's sun shadow. Short of units it points at the outback's (unit 2: a sampler must point at a texture of its type), and Rio goes without.
        _program.Uniform(Loc("uShadowRio"), MaxTextureUnits > 20 ? 20 : 2);
        // Unit 19: the flares' shadows. Short of units it reads the floodlights' (a shadow sampler must point at a shadow texture), and they go without.
        _program.Uniform(Loc("uFlareShadow"), MaxTextureUnits > FlareShadowUnit ? FlareShadowUnit : 11);
        _program.Uniform(Loc("uFlareCount"), 0);
        // Unit 21: the light on a smoke screen's puffs (DrawParticles), bound there for good. Short of units the puffs are lit a pixel at a time, as other particles are.
        _uParticleBake = Loc("uParticleBake");
        _program.Uniform(_uParticleBake, 0);
        _smokeLightReady = MaxTextureUnits > SmokeLightUnit;
        if (_smokeLightReady)
        {
            ActiveTexture(GL_TEXTURE0 + (uint)SmokeLightUnit);
            _smokeLight.EnsureSize(new int2(SmokeLightSize, SmokeLightSize));   // (leaves its texture bound to the unit)
            ActiveTexture(GL_TEXTURE0);
            _program.Uniform(Loc("uSmokeLight"), SmokeLightUnit);
        }
        _uFlarePos = Loc("uFlarePos[0]");
        _uFlareColor = Loc("uFlareColor[0]");
        _uFlareCount = Loc("uFlareCount");
        _program.Uniform(Loc("uSunJungle"), float3.Normalize(new float3(0.30f, 0.88f, 0.36f)));
        _program.Uniform(Loc("uSunColorJungle"), JungleSunColor);
        _program.Uniform(Loc("uWhiteout"), new float3(0.62f, 0.66f, 0.70f));
        // Leaf cards antialias their cut-out edges with alpha-to-coverage (every other fragment writes alpha = 1).
        Enable((GlCapability)GL_SAMPLE_ALPHA_TO_COVERAGE);
        _program.Uniform(_uTextureSpace, float4x4.Identity);
        _program.Uniform(_uSkin, 0);
        _program.Uniform(_uWoundCount, 0);
        _program.Uniform(_uWoundStyle, 0);
        _program.Uniform(Loc("uViewModel"), 0);
        _program.Uniform(Loc("uFlash"), float4.Zero);
        _program.Uniform(Loc("uExposure"), 1f);
        _program.Uniform(Loc("uSkyDisplayYard"), 1f);
        _program.Uniform(_uRoughMetal, new float2(0.6f, 0f));
        // Until SetEnvironment: a plausible sun for each area and the procedural sky.
        _program.Uniform(Loc("uSunColorYard"), new float3(1.0f, 0.96f, 0.88f) * 1.2f);
        _program.Uniform(Loc("uSunColorOutback"), new float3(1.3f, 1.18f, 1.0f) * 3f);
        _program.Uniform(Loc("uSkyMips"), 8f);

        if (_uRipples < 0 || _uRippleCount < 0 || _uTime < 0 || _uPortalWorldToLocal < 0)
            Log.Write("[WARN] Water uniforms were not found in the shader; portal ripples won't show.");
    }

    private int Loc(string name)
    {
        if (!_locations.TryGetValue(name, out var location))
        {
            location = _program.GetUniformLocation(System.Text.Encoding.UTF8.GetBytes(name + "\0"));
            _locations[name] = location;
        }

        return location;
    }

    /// <summary>The graphics driver in use, e.g. "NVIDIA GeForce RTX 3070/PCIe/SSE2 (4.6.0 NVIDIA 560.94)".</summary>
    public static unsafe string DriverDescription
    {
        get
        {
            var renderer = System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)GetString(GL_RENDERER)) ?? "?";
            var version = System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)GetString(GL_VERSION)) ?? "?";
            return $"{renderer} (OpenGL {version})";
        }
    }

    /// <summary>
    /// Hooks up the texture library and the two outdoor skies (either may be null: then those surfaces and skies
    /// stay procedural). Binds the road's texture set and the sky cubemaps for the rest of the run.
    /// </summary>
    public void SetEnvironment(TextureLibrary textures, SkyEnvironment? yard, SkyEnvironment? outback)
    {
        _textures = textures;
        _boundMaterial = (Material)(-1);

        if (textures.TryGet(Material.DirtRoad, out var road))
        {
            Bind(6, road.Albedo);
            Bind(7, road.Normal);
            Bind(8, road.Orm);
            _program.Uniform(Loc("uHasRoad"), 1);
            _program.Uniform(Loc("uRoadTile"), road.Tile);
        }

        // (The firing range on its own has only the outback's sky: nothing there is lit by the yard's, so it can stand in.)
        yard ??= outback;
        if (yard is not null && outback is not null)
        {
            ActiveTexture(GL_TEXTURE0 + 9u);
            BindTexture(GL_TEXTURE_CUBE_MAP, yard.Cubemap.Id);
            ActiveTexture(GL_TEXTURE0 + 10u);
            BindTexture(GL_TEXTURE_CUBE_MAP, outback.Cubemap.Id);
            ActiveTexture(GL_TEXTURE0);
            Enable((GlCapability)GL_TEXTURE_CUBE_MAP_SEAMLESS);

            // At night the yard's sky only gives a dim ambient light (the floodlights do the rest) and there's no sun.
            var kYard = yard.IsNight ? yard.Normalization(YardNightAmbient, 0f) : yard.Normalization(YardWhiteLevel, YardSunFactor);
            var kOutback = outback.Normalization(OutbackWhiteLevel, 1f);
            _program.Uniform(Loc("uSkyScaleYard"), kYard);
            _program.Uniform(Loc("uSkyDisplayYard"), yard.IsNight ? NightSkyDisplay : 1f);
            _program.Uniform(Loc("uSkyScaleOutback"), kOutback);
            _program.Uniform(Loc("uSunColorYard"), yard.IsNight ? float3.Zero : yard.SunIrradiance * (YardSunFactor * kYard));
            _program.Uniform(Loc("uSunColorOutback"), outback.SunIrradiance * kOutback);
            _program.Uniform(Loc("uSkyMips"), (float)int.Min(yard.MipLevels, outback.MipLevels));
            _program.Uniform(Loc("uSkyReady"), 1);
            Log.Write($"Skies: yard x{kYard:0.0000}, outback x{kOutback:0.0000}, {yard.MipLevels} mip levels.");
        }
    }

    private static void Bind(int unit, GlTexture texture)
    {
        ActiveTexture(GL_TEXTURE0 + (uint)unit);
        BindTexture(GL_TEXTURE_2D, texture.Id);
        ActiveTexture(GL_TEXTURE0);
    }

    /// <summary>
    /// Starts rendering a view of the world: binds <paramref name="target"/> (null = the window), clears it and sets the camera.
    /// <paramref name="clipPlane"/> (xyz = normal, w = offset) discards everything on its negative side.
    /// <paramref name="scissor"/> (x, y, width, height in pixels) limits drawing to that part of the target (a portal's
    /// view is only ever seen through the portal's outline on screen).
    /// </summary>
    /// <param name="relativeTo">When set, <paramref name="viewProjection"/> is that of the camera moved to the origin, and everything is projected relative to it. Far out (the jungle is 20 km from the origin) the usual matrix cancels huge numbers: the depths come out wrong by centimetres, and more with distance, whenever the view is at a slant to the x axis, so anything fine against a wall flickers or vanishes.</param>
    public void BeginView(RenderTarget? target, int2 size, in float4x4 viewProjection, float3 cameraPosition, float4? clipPlane = null,
        int4? scissor = null, bool relativeTo = false)
    {
        BindFramebuffer(GL_FRAMEBUFFER, target?.Framebuffer ?? default);
        Viewport(0, 0, size.X, size.Y);
        _viewTarget = target;
        _viewSize = size;
        _viewScissored = scissor is not null;
        if (scissor is { } s)
        {
            Enable(GlCapability.ScissorTest);
            Scissor(s.X, s.Y, s.Z, s.W);
        }
        else
        {
            Disable(GlCapability.ScissorTest);
        }
        Enable(GlCapability.DepthTest);
        DepthFunc(GlFunc.Less);
        Enable(GlCapability.CullFace);

        // Without a clip plane, use one that keeps everything (0*x + 1 > 0). Disabling the clip distance alone isn't
        // enough on every driver: the plane left over from rendering a portal's view would still cut the main view.
        Enable(GlCapability.ClipDistance0);
        _program.Uniform(_uClipPlane, clipPlane ?? new float4(0, 0, 0, 1));

        ClearColor(new float4(SkyColor, 1f));
        Clear(GlClearBit.Color | GlClearBit.Depth);

        _program.Use();
        _program.Uniform(_uScreenSize, new float2(size.X, size.Y));
        _program.Uniform(_uA2C, target is null ? 1 : 0);
        SetCamera(viewProjection, cameraPosition, relativeTo);
        SetViewVolume(viewProjection, scissor, size);
        _viewClip = clipPlane;
        SelectShadeVolumes(cameraPosition);
    }

    public void SetCamera(in float4x4 viewProjection, float3 cameraPosition, bool relativeTo = false)
    {
        _program.Uniform(_uViewProjection, viewProjection);
        _program.Uniform(_uCameraPos, cameraPosition);
        CameraPosition = cameraPosition;
        _viewProjection = viewProjection;
        _projectFrom = relativeTo ? cameraPosition : float3.Zero;
        _program.Uniform(_uProjectFrom, _projectFrom);
        SetViewVolume(viewProjection, null, default);
        _viewClip = null;
    }

    private float4x4 _viewProjection;
    private float3 _projectFrom;
    private readonly float4[] _viewPlanes = new float4[6];
    private float4? _viewClip;

    /// <summary>The sides of what's on screen (of the scissored part, if any), from the view-projection: unit normals pointing in.</summary>
    private void SetViewVolume(in float4x4 m, int4? scissor, int2 size)
    {
        var cx = new float4(m.X.X, m.Y.X, m.Z.X, m.W.X);
        var cy = new float4(m.X.Y, m.Y.Y, m.Z.Y, m.W.Y);
        var cz = new float4(m.X.Z, m.Y.Z, m.Z.Z, m.W.Z);
        var cw = new float4(m.X.W, m.Y.W, m.Z.W, m.W.W);
        float x0 = -1f, x1 = 1f, y0 = -1f, y1 = 1f;
        if (scissor is { } s && size.X > 0 && size.Y > 0)
        {
            x0 = 2f * s.X / size.X - 1f;
            x1 = 2f * (s.X + s.Z) / size.X - 1f;
            y0 = 2f * s.Y / size.Y - 1f;
            y1 = 2f * (s.Y + s.W) / size.Y - 1f;
        }

        _viewPlanes[0] = cx - cw * x0;
        _viewPlanes[1] = cw * x1 - cx;
        _viewPlanes[2] = cy - cw * y0;
        _viewPlanes[3] = cw * y1 - cy;
        _viewPlanes[4] = cz + cw;
        _viewPlanes[5] = cw - cz;
        for (var i = 0; i < _viewPlanes.Length; i++)
        {
            var p = _viewPlanes[i];
            var length = float.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);
            _viewPlanes[i] = length > 1e-9f ? p / length : new float4(0, 0, 0, 1);
        }
    }

    /// <summary>
    /// False if <paramref name="box"/> is certainly out of the view being drawn: off screen (outside its scissor) or behind
    /// its clip plane. (With a little room to spare: far out, a float is only good to a few millimetres.)
    /// </summary>
    public bool InView(in Aabb box)
    {
        const float spare = -0.25f;
        var min = box.Min - _projectFrom;
        var max = box.Max - _projectFrom;
        foreach (var p in _viewPlanes)
        {
            if (p.X * (p.X > 0 ? max.X : min.X) + p.Y * (p.Y > 0 ? max.Y : min.Y) + p.Z * (p.Z > 0 ? max.Z : min.Z) + p.W < spare)
                return false;
        }

        if (_viewClip is { } c)
            return c.X * (c.X > 0 ? box.Max.X : box.Min.X) + c.Y * (c.Y > 0 ? box.Max.Y : box.Min.Y) + c.Z * (c.Z > 0 ? box.Max.Z : box.Min.Z) + c.W >= spare;
        return true;
    }

    /// <summary>
    /// Starts drawing the view model (the gun and hands) relative to the camera: draw it with a camera at the origin
    /// (<paramref name="view"/> with a zero position), and the camera's real position is added back for the lighting.
    /// Far out (the jungle is 20 km from the origin) a float only holds a position to a couple of millimetres, which
    /// is nothing on the world but several pixels on a gun 30 cm from the eye: its corners would snap about and
    /// shimmer as you moved. <see cref="EndViewModel"/> goes back to world space.
    /// </summary>
    /// <summary>Fills <paramref name="target"/> with black: a portal that opens onto nothing.</summary>
    public void ClearToBlack(RenderTarget target)
    {
        BindFramebuffer(GL_FRAMEBUFFER, target.Framebuffer);
        Disable(GlCapability.ScissorTest);
        ClearColor(new float4(0f, 0f, 0f, 1f));
        Clear(GlClearBit.Color | GlClearBit.Depth);
    }

    public void BeginViewModel(in Camera view, float aspect)
    {
        var relative = view with { Position = float3.Zero };
        _program.Uniform(_uViewProjection, relative.View * relative.Projection(aspect));
        _program.Uniform(_uProjectFrom, float3.Zero);
        _program.Uniform(_uViewOrigin, view.Position);
        SelectShadeVolumes(view.Position);   // (whatever view was drawn last: a camera's or a mirror's, perhaps)
    }

    public void EndViewModel()
    {
        _program.Uniform(_uViewProjection, _viewProjection);
        _program.Uniform(_uProjectFrom, _projectFrom);
        _program.Uniform(_uViewOrigin, float3.Zero);
    }

    /// <summary>
    /// Starts drawing into one rectangle of the window, over whatever the overlay has put there (a picture on a menu: the
    /// gun locker's guns). <paramref name="rect"/> is x, y, width, height in pixels from the bottom left; depth is cleared
    /// in it. Draw as for the view model: with <paramref name="viewProjection"/> a camera at the origin, the lighting is
    /// that at <paramref name="lightAt"/> (the player's eye, where the gun in their hands is lit). <see cref="EndInset"/>
    /// goes back to the overlay over the whole window.
    /// </summary>
    public void BeginInset(int4 rect, in float4x4 viewProjection, float3 lightAt)
    {
        Viewport(rect.X, rect.Y, rect.Z, rect.W);
        Enable(GlCapability.ScissorTest);
        Scissor(rect.X, rect.Y, rect.Z, rect.W);
        Clear(GlClearBit.Depth);
        Enable(GlCapability.DepthTest);
        DepthFunc(GlFunc.Less);
        Enable(GlCapability.CullFace);
        _program.Uniform(_uViewProjection, viewProjection);
        _program.Uniform(_uCameraPos, lightAt);
        _program.Uniform(_uProjectFrom, float3.Zero);
        _program.Uniform(_uViewOrigin, lightAt);
        SelectShadeVolumes(lightAt);
        SetViewModel(true);
        _program.Uniform(Loc("uStudio"), 1);   // lit the same by day and by night, whatever level it's in
    }

    public void EndInset(int2 windowSize)
    {
        _program.Uniform(Loc("uStudio"), 0);
        SetViewModel(false);
        SetTextureSpace(float4x4.Identity);
        _program.Uniform(_uViewOrigin, float3.Zero);
        Viewport(0, 0, windowSize.X, windowSize.Y);
        BeginOverlay();
    }

    /// <summary>Clears depth so the next draws (e.g. the weapon view model) render on top of the world.</summary>
    public void ClearDepth()
    {
        Disable(GlCapability.ScissorTest);   // (all of it, whatever view was drawn last)
        Clear(GlClearBit.Depth);
    }

    /// <summary>Starts a frame with no world in it (the start menu): the whole window, cleared to black.</summary>
    public void ClearWindow(int2 size)
    {
        BindFramebuffer(GL_FRAMEBUFFER, default);
        Viewport(0, 0, size.X, size.Y);
        Disable(GlCapability.ScissorTest);
        ClearColor(new float4(0f, 0f, 0f, 1f));
        Clear(GlClearBit.Color | GlClearBit.Depth);
        _program.Use();
        _program.Uniform(_uClipPlane, new float4(0, 0, 0, 1));
        _program.Uniform(_uScreenSize, new float2(size.X, size.Y));
        _program.Uniform(_uA2C, 1);
    }

    /// <summary>The shader's areas (a bit for each: see areaOf) graded to moonlight, from now on.</summary>
    public void SetNightAreas(int areas) => _program.Uniform(Loc("uNightAreas"), areas);

    /// <summary>
    /// What everything drawn from now on is seen as, through a scope: set it round just the views seen through the scope
    /// (and any portals' and mirrors' views inside them, which then show as they are), and back to off before the overlay.
    /// </summary>
    public void SetVision(ScopeVision vision)
    {
        Vision = vision;
        _program.Uniform(Loc("uVision"), (int)vision);
    }

    /// <summary>What everything drawn now is seen as (see <see cref="SetVision"/>): what only shows through goggles or a scope checks it.</summary>
    public ScopeVision Vision { get; private set; }

    /// <summary>Switches to a 2D overlay pass: no depth test, coordinates are normalised device coordinates.</summary>
    public void BeginOverlay()
    {
        Disable(GlCapability.DepthTest);
        Disable(GlCapability.ScissorTest);
        Disable(GlCapability.CullFace);
        SetCamera(float4x4.Identity, float3.Zero);
    }

    /// <summary>
    /// Sets the space triplanar textures are mapped in. Use a moving object's world-to-local matrix (rigid) so its
    /// surface detail moves with it; reset to <see cref="float4x4.Identity"/> afterwards.
    /// </summary>
    public void SetTextureSpace(in float4x4 worldToTexture) => _program.Uniform(_uTextureSpace, worldToTexture);

    /// <summary>
    /// Draws a skinned part: each vertex is blended from <paramref name="world"/> to <paramref name="skinWorld"/> by
    /// its weight, stored in the tangent's w as sign x (1 + weight) (a sleeve whose cuff follows the hand).
    /// </summary>
    public void DrawSkinnedPart(ModelPart part, in float4x4 world, in float4x4 skinWorld, float3 tint)
    {
        _program.Uniform(_uSkin, 1);
        _program.Uniform(_uSkinModel, skinWorld);
        DrawPart(part, world, tint);
        _program.Uniform(_uSkin, 0);
    }

    /// <summary>Marks what's drawn next as the view model (the gun and hands): no snow settles on it.</summary>
    public void SetViewModel(bool viewModel) => _program.Uniform(Loc("uViewModel"), viewModel ? 1 : 0);

    /// <summary>A shot's flash of light at <paramref name="position"/> (strength 0: none), for this frame.</summary>
    public void SetFlash(float3 position, float strength) => _program.Uniform(Loc("uFlash"), new float4(position, strength));

    /// <summary>
    /// The flares burning, for this frame: xyz position and w range; rgb irradiance 1 m away and a its row in the shadow
    /// atlas (below 0: no shadows yet). At most <see cref="FlareShadowAtlas.MaxFlares"/>.
    /// </summary>
    public void SetFlares(ReadOnlySpan<float4> positions, ReadOnlySpan<float4> colors)
    {
        var count = int.Min(positions.Length, FlareShadowAtlas.MaxFlares);
        _program.Uniform(_uFlareCount, count);
        if (count == 0)
            return;
        _program.UniformArray(_uFlarePos, positions[..count]);
        if (MaxTextureUnits > FlareShadowUnit)
        {
            _program.UniformArray(_uFlareColor, colors[..count]);
            return;
        }

        // No unit for the shadow atlas: shadowless.
        Span<float4> shadowless = stackalloc float4[count];
        for (var i = 0; i < count; i++)
            shadowless[i] = colors[i] with { W = -1f };
        _program.UniformArray(_uFlareColor, shadowless);
    }

    /// <summary>Binds the flares' shadow atlas (texture unit 19) for all later drawing.</summary>
    public void SetFlareShadows(FlareShadowAtlas atlas)
    {
        if (MaxTextureUnits <= FlareShadowUnit)
            return;
        ActiveTexture(GL_TEXTURE0 + (uint)FlareShadowUnit);
        BindTexture(GL_TEXTURE_2D, atlas.DepthTexture.Id);
        ActiveTexture(GL_TEXTURE0);
        // A face's depth is a + b / distance along its axis: read off its projection, whatever the convention.
        _program.Uniform(Loc("uFlareDepth"), DepthOf(FlareShadowAtlas.ViewProjection(float3.Zero, 0)));
        _program.Uniform(Loc("uFlareDepthSky"), DepthOf(FlareShadowAtlas.ViewProjection(float3.Zero, 0, sky: true)));

        static float2 DepthOf(in float4x4 projection)
        {
            var near = float4.Transform(new float4(1f, 0f, 0f, 1f), projection);
            var far = float4.Transform(new float4(2f, 0f, 0f, 1f), projection);
            float z1 = near.Z / near.W, z2 = far.Z / far.W;
            var b = (z1 - z2) * 2f;
            return new float2(z1 - b, b);
        }
        _program.Uniform(Loc("uFlareTile"), new float2(FlareShadowAtlas.FaceTangent, 1f / atlas.TileSize));
    }

    /// <summary>
    /// Starts rendering a face of a flare's shadow map into its tile of <paramref name="atlas"/>. Draw the static level,
    /// then call <see cref="EndShadowPass"/>.
    /// </summary>
    public void BeginFlareShadowTile(FlareShadowAtlas atlas, int flare, int face, in float4x4 viewProjection)
    {
        BindFramebuffer(GL_FRAMEBUFFER, atlas.Framebuffer);
        var origin = atlas.TileOrigin(flare, face);
        Viewport(origin.X, origin.Y, atlas.TileSize, atlas.TileSize);
        Enable(GlCapability.DepthTest);
        DepthFunc(GlFunc.Less);
        ColorMask(false, false, false, false);
        Enable(GlCapability.ScissorTest);   // (clear just this tile: the other flares' are in use)
        Scissor(origin.X, origin.Y, atlas.TileSize, atlas.TileSize);
        Clear(GlClearBit.Depth);
        Disable(GlCapability.ScissorTest);
        Disable(GlCapability.CullFace);
        Enable(GlCapability.PolygonOffsetFill);
        PolygonOffset(2f, 4f);
        Enable(GlCapability.ClipDistance0);
        _program.Use();
        _program.Uniform(_uClipPlane, new float4(0, 0, 0, 1));
        _program.Uniform(_uShadowPass, 1);
        SetCamera(viewProjection, float3.Zero);
    }

    /// <summary>Sets the point lights (xyz = position, w = intensity). They stay set until changed.</summary>
    public void SetLights(ReadOnlySpan<float4> lights)
    {
        var count = int.Min(lights.Length, MaxLights);
        _program.Uniform(_uLightCount, count);
        if (count > 0)
            _program.UniformArray(_uLights, lights[..count]);
    }

    /// <summary>
    /// Starts rendering depth into <paramref name="map"/> from its sun. Draw the static level, then call
    /// <see cref="EndShadowPass"/>.
    /// </summary>
    public void BeginShadowPass(ShadowMap map)
    {
        BindFramebuffer(GL_FRAMEBUFFER, map.Framebuffer);
        Viewport(0, 0, map.Size, map.Size);
        Disable(GlCapability.ScissorTest); // a portal view's scissor would clip the shadow map
        Enable(GlCapability.DepthTest);
        DepthFunc(GlFunc.Less);
        Disable(GlCapability.CullFace); // thin walls and sheets cast shadows from both sides
        Enable(GlCapability.PolygonOffsetFill);
        PolygonOffset(2f, 4f);
        Enable(GlCapability.ClipDistance0);
        _program.Use();
        _program.Uniform(_uClipPlane, new float4(0, 0, 0, 1));
        _program.Uniform(_uShadowPass, 1);
        ColorMask(false, false, false, false);
        Clear(GlClearBit.Depth);
        SetCamera(map.ViewProjection, float3.Zero);
    }

    public void EndShadowPass()
    {
        ColorMask(true, true, true, true);
        Disable(GlCapability.PolygonOffsetFill);
        Enable(GlCapability.CullFace);
        _program.Uniform(_uShadowPass, 0);
        BindFramebuffer(GL_FRAMEBUFFER, default);
    }

    /// <summary>Binds the finished shadow maps (texture units 1 and 2) and their suns for all later drawing.</summary>
    public void SetShadowMaps(ShadowMap yard, ShadowMap outback)
    {
        ActiveTexture(GL_TEXTURE1);
        BindTexture(GL_TEXTURE_2D, yard.DepthTexture.Id);
        ActiveTexture(GL_TEXTURE2);
        BindTexture(GL_TEXTURE_2D, outback.DepthTexture.Id);
        ActiveTexture(GL_TEXTURE0);

        _program.Uniform(Loc("uShadowYardVP"), yard.ViewProjection);
        _program.Uniform(Loc("uShadowOutbackVP"), outback.ViewProjection);
        _program.Uniform(Loc("uSunYard"), yard.SunDirection);
        _program.Uniform(Loc("uSunOutback"), outback.SunDirection);
        _program.Uniform(Loc("uShadowsReady"), 1);
    }

    /// <summary>
    /// Starts rendering floodlight <paramref name="tile"/>'s shadow map into its quarter of <paramref name="atlas"/>
    /// (tile 0 clears the whole atlas first). Draw the static level, then call <see cref="EndShadowPass"/>.
    /// </summary>
    public void BeginShadowTile(FloodShadowAtlas atlas, int tile, in float4x4 viewProjection)
    {
        BindFramebuffer(GL_FRAMEBUFFER, atlas.Framebuffer);
        Disable(GlCapability.ScissorTest);
        Enable(GlCapability.DepthTest);
        DepthFunc(GlFunc.Less);
        ColorMask(false, false, false, false);
        if (tile == 0)
        {
            Viewport(0, 0, atlas.Size, atlas.Size);
            Clear(GlClearBit.Depth);
        }
        var origin = atlas.TileOrigin(tile);
        Viewport(origin.X, origin.Y, atlas.Size / 2, atlas.Size / 2);
        Disable(GlCapability.CullFace);
        Enable(GlCapability.PolygonOffsetFill);
        PolygonOffset(2.5f, 6f);
        Enable(GlCapability.ClipDistance0);
        _program.Use();
        _program.Uniform(_uClipPlane, new float4(0, 0, 0, 1));
        _program.Uniform(_uShadowPass, 1);
        SetCamera(viewProjection, float3.Zero);
    }

    /// <summary>
    /// Starts a depth-only pass over the opaque things about to be drawn: afterwards <see cref="EndDepthPrepass"/> makes the
    /// colour pass shade only what ends up in front at each pixel, however many layers of leaves are behind it.
    /// </summary>
    public void BeginDepthPrepass()
    {
        ColorMask(false, false, false, false);
        _program.Uniform(_uShadowPass, 1);
    }

    public void EndDepthPrepass()
    {
        ColorMask(true, true, true, true);
        _program.Uniform(_uShadowPass, 0);
        DepthFunc(GlFunc.LessEqual);   // (the same surfaces again: BeginView sets it back)
    }

    /// <summary>The yard's floodlights (at most four) and, once rendered, their shadow atlas (texture unit 11).</summary>
    public void SetFloodLights(ReadOnlySpan<FloodLight> lights, FloodShadowAtlas? atlas)
    {
        var count = int.Min(lights.Length, FloodShadowAtlas.MaxLights);
        for (var i = 0; i < count; i++)
        {
            var l = lights[i];
            _program.Uniform(Loc($"uFloodPos[{i}]"), new float4(l.Position, float.Cos(float.DegreesToRadians(l.OuterDegrees))));
            _program.Uniform(Loc($"uFloodDir[{i}]"), new float4(l.Direction, float.Cos(float.DegreesToRadians(l.InnerDegrees))));
            _program.Uniform(Loc($"uFloodColor[{i}]"), new float4(l.Color * l.Intensity, 0f));
            _program.Uniform(Loc($"uFloodVP[{i}]"), l.ViewProjection);
        }
        _program.Uniform(Loc("uFloodCount"), count);
        if (atlas is not null)
        {
            ActiveTexture(GL_TEXTURE0 + 11u);
            BindTexture(GL_TEXTURE_2D, atlas.DepthTexture.Id);
            ActiveTexture(GL_TEXTURE0);
        }
        _program.Uniform(Loc("uFloodShadowReady"), atlas is null ? 0 : 1);
    }

    /// <summary>Lamps that glow in the yard's night air (at most 16).</summary>
    public void SetGlows(ReadOnlySpan<GlowPoint> glows)
    {
        var count = int.Min(glows.Length, 16);
        for (var i = 0; i < count; i++)
        {
            _program.Uniform(Loc($"uGlow[{i}]"), new float4(glows[i].Position, glows[i].Intensity));
            _program.Uniform(Loc($"uGlowColor[{i}]"), new float4(glows[i].Color, glows[i].Range));
        }
        _program.Uniform(Loc("uGlowCount"), count);
        _program.Uniform(Loc("uGlowScatter"), NightGlowScatter);
        _program.Uniform(Loc("uGlare"), NightGlare);
    }

    /// <summary>How bright a white surface in full sun ends up on the container ship, and how much sun the overcast lets through.</summary>
    public static float ShipWhiteLevel = 1.15f;
    public static float ShipSunFactor = 0.30f;

    /// <summary>
    /// The container ship's sky (texture unit 12) and the shadow map of its sun (unit 13). With no sky, the ship
    /// falls back to the yard's.
    /// </summary>
    public void SetShipEnvironment(SkyEnvironment? sky, ShadowMap? shadow)
    {
        if (sky is not null)
        {
            ActiveTexture(GL_TEXTURE0 + 12u);
            BindTexture(GL_TEXTURE_CUBE_MAP, sky.Cubemap.Id);
            ActiveTexture(GL_TEXTURE0);
            var k = sky.Normalization(ShipWhiteLevel, ShipSunFactor);
            _program.Uniform(Loc("uSkyScaleShip"), k);
            _program.Uniform(Loc("uSunColorShip"), sky.SunIrradiance * (ShipSunFactor * k));
            _program.Uniform(Loc("uShipReady"), 1);
            Log.Write($"Ship sky x{k:0.0000}.");
        }
        if (shadow is not null)
        {
            ActiveTexture(GL_TEXTURE0 + 13u);
            BindTexture(GL_TEXTURE_2D, shadow.DepthTexture.Id);
            ActiveTexture(GL_TEXTURE0);
            _program.Uniform(Loc("uShadowShipVP"), shadow.ViewProjection);
            _program.Uniform(Loc("uSunShip"), shadow.SunDirection);
        }
    }

    /// <summary>The jungle's sun (irradiance face-on): strong, warm tropical light, softened a little by the haze.</summary>
    public static readonly float3 JungleSunColor = new float3(1.0f, 0.94f, 0.80f) * 3.0f;

    /// <summary>
    /// The jungle: the shadow map of its sun (texture unit 14) and the clearing that's open to the sky, an ellipse
    /// round <paramref name="clearingCenter"/> (x, z radii <paramref name="clearingRadii"/>). Under the canopy outside it,
    /// the sky's light is mostly lost in the leaves.
    /// </summary>
    public void SetJungleEnvironment(ShadowMap? shadow, float3 clearingCenter, float2 clearingRadii)
    {
        _program.Uniform(Loc("uJungleClearing"), new float4(clearingCenter.X, clearingCenter.Z, clearingRadii.X, clearingRadii.Y));
        _program.Uniform(Loc("uSunColorJungle"), JungleSunColor);
        if (shadow is null)
            return;
        ActiveTexture(GL_TEXTURE0 + 14u);
        BindTexture(GL_TEXTURE_2D, shadow.DepthTexture.Id);
        ActiveTexture(GL_TEXTURE0);
        _program.Uniform(Loc("uShadowJungleVP"), shadow.ViewProjection);
        _program.Uniform(Loc("uSunJungle"), shadow.SunDirection);
        _program.Uniform(Loc("uJungleShadowReady"), 1);
    }

    /// <summary>Office lights the police station and the prison can have between them (MAX_OFFICE in the fragment shader).</summary>
    public const int MaxOfficeLights = 256;

    private GlBuffer _officeBuffer;
    private bool _hasOfficeBuffer;

    /// <summary>
    /// The police station's and the prison's lights (areas 7 and 10): their ceiling panels (which shine down only) and
    /// any other lights in them, each lighting only inside its box, and the bounce light that fills the rooms. They go
    /// into a uniform buffer (the "OfficeLights" block, binding 1), the police station's first: a point in either
    /// building only goes through its own building's lights.
    /// </summary>
    public void SetOfficeLights(ReadOnlySpan<OfficeLight> lights, float3 ambient)
    {
        // The police station's first, then the prison's (and the flats'), then Rio's (a stable sort).
        var sorted = lights.ToArray().OrderBy(l => l.Position.Y < RioBelowY ? 2 : l.Position.Y < PrisonBelowY ? 1 : 0).ToArray();
        var count = int.Min(sorted.Length, MaxOfficeLights);
        if (sorted.Length > MaxOfficeLights)
            Log.Write($"[WARN] {sorted.Length} office lights; only the first {MaxOfficeLights} are used.");
        // std140: four arrays of vec4, each MaxOfficeLights long.
        var data = new float[MaxOfficeLights * 4 * 4];
        void Put(int array, int i, float4 v)
        {
            var at = (array * MaxOfficeLights + i) * 4;
            data[at] = v.X;
            data[at + 1] = v.Y;
            data[at + 2] = v.Z;
            data[at + 3] = v.W;
        }
        var split = 0;
        var rioStart = count;
        for (var i = 0; i < count; i++)
        {
            var l = sorted[i];
            Put(0, i, new float4(l.Position, l.Range));
            Put(1, i, new float4(l.Color * l.Intensity, l.Panel ? 1f : 0f));
            Put(2, i, new float4(l.Bounds.Min, 0f));
            Put(3, i, new float4(l.Bounds.Max, 0f));
            if (l.Position.Y >= PrisonBelowY)
                split = i + 1;
            if (l.Position.Y < RioBelowY && rioStart == count)
                rioStart = i;
        }
        if (!_hasOfficeBuffer)
        {
            _officeBuffer = GenBuffer();
            _hasOfficeBuffer = true;
        }
        _officeBuffer.Bind(GlBindTarget.UniformBuffer);
        BufferData(GlBindTarget.UniformBuffer, (ReadOnlySpan<float>)data, GlBufferUsage.StaticDraw);
        var block = GetUniformBlockIndex(_program, "OfficeLights"u8);
        if (block == uint.MaxValue)
            Log.Write("[WARN] The shader has no OfficeLights block: the police station and the prison will be unlit.");
        UniformBlockBinding(_program, block, 1);
        BindBufferBase(GL_UNIFORM_BUFFER, 1, _officeBuffer);
        _program.Uniform(Loc("uOfficeCount"), count);
        _program.Uniform(Loc("uOfficeSplit"), split);
        _program.Uniform(Loc("uOfficeRioStart"), rioStart);
        _program.Uniform(Loc("uOfficeAmbient"), ambient);
        Log.Write($"[RENDER] {count} office lights ({split} in the police station, {rioStart - split} in the prison and the flats, {count - rioStart} in Rio)");
    }

    /// <summary>Below this height a light (or a point) is in the prison (PRISON_BELOW_Y in the fragment shader).</summary>
    private const float PrisonBelowY = -1500f;

    /// <summary>Below this height a light is in Rio (RIO_BELOW_Y in the fragment shader).</summary>
    private const float RioBelowY = -3500f;

    /// <summary>
    /// The police station's occlusion map (texture unit 15): a depth map from straight above of the furniture, sampled
    /// over a disc <paramref name="radius"/> metres across for soft shade under it.
    /// </summary>
    public void SetOfficeOcclusion(ShadowMap map, float halfExtent, float radius)
    {
        ActiveTexture(GL_TEXTURE0 + 15u);
        BindTexture(GL_TEXTURE_2D, map.DepthTexture.Id);
        ActiveTexture(GL_TEXTURE0);
        _program.Uniform(Loc("uOfficeOccVP"), map.ViewProjection);
        _program.Uniform(Loc("uOfficeOccRadius"), radius / (2f * halfExtent));
        _program.Uniform(Loc("uOfficeOccReady"), 1);
    }

    /// <summary>Texture units the fragment shader can use (GL_MAX_TEXTURE_IMAGE_UNITS).</summary>
    public int MaxTextureUnits { get; }

    /// <summary>
    /// The factory grounds' sun shadow map (texture unit 16; skipped, so no shadows round the factory, on a GPU with
    /// only 16 units). The grounds use the outback's sun and sky.
    /// </summary>
    public void SetFactoryShadow(ShadowMap shadow)
    {
        if (MaxTextureUnits <= 16)
        {
            Log.Write($"[WARN] Only {MaxTextureUnits} texture units: no sun shadows round the factory.");
            return;
        }
        ActiveTexture(GL_TEXTURE0 + 16u);
        BindTexture(GL_TEXTURE_2D, shadow.DepthTexture.Id);
        ActiveTexture(GL_TEXTURE0);
        _program.Uniform(Loc("uShadowFactoryVP"), shadow.ViewProjection);
        _program.Uniform(Loc("uFactoryShadowReady"), 1);
    }

    /// <summary>
    /// The prison (areas 9 and 10): the boxes of its walk-through rooms (inside them it's lit like an office, by the
    /// <see cref="SetOfficeLights"/> fittings), its sun, and the sun's shadow map on texture unit 17 where the GPU has
    /// one (otherwise the prison grounds have no sun shadows).
    /// </summary>
    public void SetPrisonEnvironment(ReadOnlySpan<Aabb> rooms, float3 sunDirection, float3 sunColor, ShadowMap? shadow)
    {
        var count = int.Min(rooms.Length, MaxPrisonRooms);
        if (rooms.Length > MaxPrisonRooms)
            Log.Write($"[WARN] {rooms.Length} prison rooms; only the first {MaxPrisonRooms} are lit as interiors.");
        for (var i = 0; i < count; i++)
        {
            _program.Uniform(Loc($"uPrisonRoomMin[{i}]"), new float4(rooms[i].Min, 0f));
            _program.Uniform(Loc($"uPrisonRoomMax[{i}]"), new float4(rooms[i].Max, 0f));
        }
        _program.Uniform(Loc("uPrisonRoomCount"), count);
        _program.Uniform(Loc("uSunPrison"), float3.Normalize(sunDirection));
        _program.Uniform(Loc("uSunColorPrison"), sunColor);
        if (shadow is null)
            return;
        if (MaxTextureUnits <= 17)
        {
            Log.Write($"[WARN] Only {MaxTextureUnits} texture units: no sun shadows in the prison.");
            return;
        }
        ActiveTexture(GL_TEXTURE0 + 17u);
        BindTexture(GL_TEXTURE_2D, shadow.DepthTexture.Id);
        ActiveTexture(GL_TEXTURE0);
        _program.Uniform(Loc("uShadowPrisonVP"), shadow.ViewProjection);
        _program.Uniform(Loc("uPrisonShadowReady"), 1);
    }

    /// <summary>
    /// The block of flats (areas 11 and 12): the box of its stairwell, and its sun shadow map on texture unit 18 where the GPU
    /// has one. Its sky and sun are the prison's.
    /// </summary>
    public void SetFlatsEnvironment(in Aabb stairwell, ShadowMap shadow)
    {
        _program.Uniform(Loc("uFlatsStairMin"), stairwell.Min);
        _program.Uniform(Loc("uFlatsStairMax"), stairwell.Max);
        if (MaxTextureUnits <= 18)
        {
            Log.Write($"[WARN] Only {MaxTextureUnits} texture units: no sun shadows at the flats.");
            return;
        }
        ActiveTexture(GL_TEXTURE0 + 18u);
        BindTexture(GL_TEXTURE_2D, shadow.DepthTexture.Id);
        ActiveTexture(GL_TEXTURE0);
        _program.Uniform(Loc("uShadowFlatsVP"), shadow.ViewProjection);
        _program.Uniform(Loc("uFlatsShadowReady"), 1);
    }

    /// <summary>
    /// Rio (area 13): the outback's sky and sun, the sky turned about the vertical so its sun stands at
    /// <paramref name="sunDirection"/> (which must be the outback's sun turned the same way), and its own sun shadow
    /// map on texture unit 20 where the GPU has one.
    /// </summary>
    public void SetRioEnvironment(float3 sunDirection, float3 outbackSunDirection, ShadowMap shadow)
    {
        var sun = float3.Normalize(sunDirection);
        var outback = float3.Normalize(outbackSunDirection);
        // The turn that takes Rio's sun round to the outback's, as cos and sin.
        var turn = float.Atan2(outback.Z, outback.X) - float.Atan2(sun.Z, sun.X);
        _program.Uniform(Loc("uSunRio"), sun);
        _program.Uniform(Loc("uRioSkyTurn"), new float2(float.Cos(turn), float.Sin(turn)));
        if (MaxTextureUnits <= 20)
        {
            Log.Write($"[WARN] Only {MaxTextureUnits} texture units: no sun shadows in Rio.");
            return;
        }
        ActiveTexture(GL_TEXTURE0 + 20u);
        BindTexture(GL_TEXTURE_2D, shadow.DepthTexture.Id);
        ActiveTexture(GL_TEXTURE0);
        _program.Uniform(Loc("uShadowRioVP"), shadow.ViewProjection);
        _program.Uniform(Loc("uRioShadowReady"), 1);
    }

    public const int MaxPrisonRooms = 10;

    /// <summary>
    /// Darkens the whole frame towards black by <paramref name="amount"/> (0: untouched, 1: black), for fades. Call it
    /// after everything else is drawn (it multiplies what's on screen).
    /// </summary>
    public void DrawFade(float amount)
    {
        if (amount <= 0.001f)
            return;
        BeginOverlay();
        Enable(GlCapability.Blend);
        BlendFunc(GL_ZERO, GL_SRC_COLOR);
        var keep = 1f - float.Clamp(amount, 0f, 1f);
        DrawCube(float4x4.CreateScale(4f, 4f, 0.001f), new float3(keep), ShadeMode.Unlit);
        Disable(GlCapability.Blend);
    }

    /// <summary>
    /// Washes the whole frame towards <paramref name="color"/> by <paramref name="amount"/> (0: untouched, 1: all that
    /// colour): a flashbang's glare. Call it after everything else is drawn.
    /// </summary>
    public void DrawWhiteout(float amount, float3 color)
    {
        if (amount <= 0.001f)
            return;
        var a = float.Clamp(amount, 0f, 1f);
        BeginOverlay();
        Enable(GlCapability.Blend);
        // What's there times (1 - a), plus the colour times a. (The flattened cube covers the screen twice, front and
        // back, with culling off: so each half of that is applied in two goes.)
        BlendFunc(GL_ZERO, GL_SRC_COLOR);
        DrawCube(float4x4.CreateScale(4f, 4f, 0.001f), new float3(float.Sqrt(1f - a)), ShadeMode.Unlit);
        BlendFunc(GL_ONE, GL_ONE);
        DrawCube(float4x4.CreateScale(4f, 4f, 0.001f), color * (a * 0.5f), ShadeMode.Unlit);
        Disable(GlCapability.Blend);
    }

    /// <summary>
    /// Draws a prism sight's eyepiece over the frame, as the eye sees it held at eye relief: black from the circle of
    /// <paramref name="inner"/> (in half screen heights, round the centre; the magnified picture shows inside it) out to
    /// <paramref name="outer"/>, its blurred edge fading out over <paramref name="soft"/>, the world seen past it.
    /// Call it in the overlay pass.
    /// </summary>
    public void DrawScopeRing(float inner, float outer, float soft)
    {
        Disable(GlCapability.SampleAlphaToCoverage);
        Enable(GlCapability.Blend);
        BlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
        DrawCube(float4x4.CreateScale(4f, 4f, 0.001f), new float3(inner, outer, soft), ShadeMode.ScopeRing);
        Disable(GlCapability.Blend);
        Enable(GlCapability.SampleAlphaToCoverage);
    }

    /// <summary>The colour of the Arctic's whiteout (linear): its sky, its fog and the blowing snow.</summary>
    public void SetWhiteout(float3 color) => _program.Uniform(Loc("uWhiteout"), color);

    /// <summary>
    /// Shadowless local lights (at most 8): a bulb, the daylight down a vent, a portal's glow. Each lights only what's
    /// inside its <see cref="LocalLight.Bounds"/>.
    /// </summary>
    public void SetLocalLights(ReadOnlySpan<LocalLight> lights)
    {
        var count = int.Min(lights.Length, 12);
        for (var i = 0; i < count; i++)
        {
            var l = lights[i];
            _program.Uniform(Loc($"uLocalPos[{i}]"), new float4(l.Position, l.Range));
            _program.Uniform(Loc($"uLocalColor[{i}]"), new float4(l.Color * l.Intensity, 0f));
            _program.Uniform(Loc($"uLocalBoxMin[{i}]"), new float4(l.Bounds.Min, 0f));
            _program.Uniform(Loc($"uLocalBoxMax[{i}]"), new float4(l.Bounds.Max, 0f));
        }
        _program.Uniform(Loc("uLocalCount"), count);
    }

    /// <summary>
    /// Dim interiors in the outdoor areas (at most 64, the shader's MAX_SHADE): inside each box the sky's light fades away
    /// from its opening, down to <see cref="ShadeVolume.Floor"/>.
    /// </summary>
    /// <param name="zonesSeenFrom">
    /// The parts of the level (a bit each) that can be seen from a point. With it, each view is only given the volumes in
    /// the parts it can see (<see cref="SelectShadeVolumes"/>): the shader goes through every volume it has for every
    /// pixel, twice, and with all the levels' volumes (54 of them, 34 of those Rio's) that was half the frame's time on the
    /// graphics card everywhere outdoors (the jungle: 24 ms a frame with them all, 12 ms with its own four). Without it
    /// every view gets them all, as before.
    /// </param>
    public void SetShadeVolumes(ReadOnlySpan<ShadeVolume> volumes, Func<float3, int>? zonesSeenFrom = null)
    {
        if (_uShadeMin.Length == 0)
        {
            _uShadeMin = new int[MaxShade];
            _uShadeMax = new int[MaxShade];
            _uShadeOpening = new int[MaxShade];
            for (var i = 0; i < MaxShade; i++)
            {
                _uShadeMin[i] = Loc($"uShadeMin[{i}]");
                _uShadeMax[i] = Loc($"uShadeMax[{i}]");
                _uShadeOpening[i] = Loc($"uShadeOpening[{i}]");
            }

            _uShadeCount = Loc("uShadeCount");
        }

        _shadeVolumes = volumes.ToArray();
        _shadeZonesSeenFrom = zonesSeenFrom;
        _shadeZones = new int[_shadeVolumes.Length];
        for (var i = 0; i < _shadeVolumes.Length; i++)
            _shadeZones[i] = zonesSeenFrom?.Invoke(_shadeVolumes[i].Bounds.Center) ?? ~0;
        _shadeShownCount = -1;   // (whatever the shader has now is out of date)
        _shadeWarned = false;
        SelectShadeVolumes(CameraPosition);
    }

    /// <summary>The shader's MAX_SHADE.</summary>
    private const int MaxShade = 64;

    /// <summary>A volume this near the eye (m) is given to the view whatever part of the level it's in (the next part's, seen across the line between them).</summary>
    private const float ShadeNear = 500f;

    private int[] _uShadeMin = [], _uShadeMax = [], _uShadeOpening = [];
    private int _uShadeCount;
    private ShadeVolume[] _shadeVolumes = [];
    private int[] _shadeZones = [];
    private Func<float3, int>? _shadeZonesSeenFrom;
    private readonly int[] _shadeShown = new int[MaxShade], _shadePick = new int[MaxShade];
    private int _shadeShownCount = -1;
    private bool _shadeWarned;

    /// <summary>
    /// Gives the shader the shade volumes that matter to a view from <paramref name="eye"/>: those in the parts of the level
    /// seen from there, and any within <see cref="ShadeNear"/>. A volume only changes what's inside it (or, in the Arctic,
    /// seen through it), and nothing in one part of the level is ever drawn in a view of another: they're kilometres apart.
    /// The uniforms are only sent when the choice has changed (so, in practice, when a view is of a different place).
    /// </summary>
    /// <remarks>
    /// (Leaving out the volumes outside the view's frustum as well was tried, for Rio's 34: only a few frames a second more
    /// there (81-86 to 83-94), and not checked well enough through portals to be worth keeping, so it isn't done.)
    /// </remarks>
    private void SelectShadeVolumes(float3 eye)
    {
        var seen = _shadeZonesSeenFrom?.Invoke(eye) ?? ~0;
        var count = 0;
        var left = 0;
        for (var i = 0; i < _shadeVolumes.Length; i++)
        {
            if ((_shadeZones[i] & seen) == 0)
            {
                var b = _shadeVolumes[i].Bounds;
                var d = float3.Max(float3.Max(b.Min - eye, eye - b.Max), float3.Zero);   // (from the eye to the nearest point of the box)
                if (float3.Dot(d, d) > ShadeNear * ShadeNear)
                    continue;
            }

            if (count < MaxShade)
                _shadePick[count++] = i;
            else
                left++;
        }

        if (left > 0 && !_shadeWarned)
        {
            _shadeWarned = true;
            Log.Write($"[WARN] {count + left} shade volumes in one view, but the shader has room for {MaxShade}.");
        }

        if (count == _shadeShownCount && _shadePick.AsSpan(0, count).SequenceEqual(_shadeShown.AsSpan(0, count)))
            return;

        for (var k = 0; k < count; k++)
        {
            var v = _shadeVolumes[_shadePick[k]];
            _program.Uniform(_uShadeMin[k], new float4(v.Bounds.Min, v.Floor));
            _program.Uniform(_uShadeMax[k], new float4(v.Bounds.Max, v.NoSun ? 1f : 0f));
            _program.Uniform(_uShadeOpening[k], new float4(v.Opening, v.Reach));
            _shadeShown[k] = _shadePick[k];
        }

        _program.Uniform(_uShadeCount, count);
        _shadeShownCount = count;
    }

    /// <summary>An outdoor volume that is shaded as an enclosed space lit from its +X end (the open container).</summary>
    public void SetShadeBox(Aabb box)
    {
        _program.Uniform(Loc("uShadeBoxMin"), box.Min);
        _program.Uniform(Loc("uShadeBoxMax"), box.Max);
    }

    /// <summary>Draws a big box as sky, seen from the inside.</summary>
    public void DrawSky(in float4x4 model)
    {
        Disable(GlCapability.CullFace);
        DrawCube(model, float3.One, ShadeMode.Lit, Material.Sky);
        Enable(GlCapability.CullFace);
    }

    /// <summary>Draws the unit cube (-0.5..0.5) transformed by <paramref name="model"/>.</summary>
    public void DrawCube(in float4x4 model, float3 color, ShadeMode mode = ShadeMode.Lit, Material material = Material.Plain) =>
        DrawMesh(_cube, model, color, mode, material);

    /// <param name="roughMetal">Roughness and metalness, for untextured surfaces.</param>
    public void DrawMesh(Mesh mesh, in float4x4 model, float3 color, ShadeMode mode = ShadeMode.Lit, Material material = Material.Plain,
        float2? roughMetal = null, bool tintPaintOnly = false, TextureSet? custom = null)
    {
        var cutout = PrepareSurface(model, color, mode, material, roughMetal, tintPaintOnly, custom, mesh.HasUVs);
        if (cutout)
        {
            Disable(GlCapability.CullFace);
            mesh.Draw();
            Enable(GlCapability.CullFace);
        }
        else
        {
            mesh.Draw();
        }
    }

    /// <summary>Sets the transform and surface uniforms for the next draw; returns whether it's an alpha cut-out.</summary>
    private static readonly bool FlatShading = Profile.Has("flat");

    private bool PrepareSurface(in float4x4 model, float3 color, ShadeMode mode, Material material, float2? roughMetal,
        bool tintPaintOnly, TextureSet? custom, bool hasUVs)
    {
        var textured = UseMaterial(material, custom);
        if (FlatShading && textured)
        {
            textured = false;
            _program.Uniform(_uTextured, 0);
            _boundMaterial = (Material)(-1);
            _boundSet = null;
        }
        _program.Uniform(_uModel, model);
        _program.Uniform(_uColor, color);
        _program.Uniform(_uMode, (int)mode);
        _program.Uniform(_uUseUV, textured && hasUVs ? 1 : 0);
        _program.Uniform(_uTintMode, tintPaintOnly ? 1 : 0);
        if (!textured)
            _program.Uniform(_uRoughMetal, roughMetal ?? DefaultRoughMetal(material));
        var cutout = material == Material.Leaves && textured && hasUVs;
        _program.Uniform(_uCutout, cutout ? 1 : 0);
        return cutout;
    }

    /// <summary>
    /// Draws a Blender model. Parts whose material ends in <c>_tint</c> (paint) take <paramref name="tint"/>; the rest
    /// keep their own colours.
    /// </summary>
    public void DrawModel(Model model, in float4x4 world, float3 tint)
    {
        foreach (var part in model.Parts)
            DrawPart(part, world, tint);
    }

    public void DrawPart(ModelPart part, in float4x4 world, float3 tint) => DrawMesh(part.Mesh, world, part.Material, tint);

    /// <summary>Draws a mesh with a model part's material (see <see cref="DrawModel"/>).</summary>
    public void DrawMesh(Mesh mesh, in float4x4 world, in PartMaterial m, float3 tint) =>
        DrawMesh(mesh, world, PartColor(m, tint), ShadeMode.Lit, m.Material, new float2(m.Roughness, m.Metalness), tintPaintOnly: m.TakesInstanceTint, custom: m.Custom);

    private float3 PartColor(in PartMaterial m, float3 tint)
    {
        var textured = m.Custom is not null || (_textures is not null && _textures.TryGet(m.Material, out _));
        if (m.TakesInstanceTint)
            return tint;
        if (m.Custom is not null)
            return m.Color;
        if (textured && m.Color.X > 0.98f && m.Color.Y > 0.98f && m.Color.Z > 0.98f)
            return RefTint(m.Material); // a plain white factor in Blender: the texture as baked
        return m.Color;
    }

    /// <summary>
    /// Draws a skinned model at level of detail <paramref name="lod"/>, posed by <paramref name="bones"/> (its
    /// skinning matrices, see <see cref="Skeleton.ComputeSkin"/>). Parts whose material ends in <c>_tint</c> (the skin)
    /// take <paramref name="tint"/>.
    /// </summary>
    /// <param name="wounds">Bullet wounds: centre in the bind pose and hole radius (xyz, w).</param>
    /// <param name="woundInfo">Per wound: age in seconds, a random seed, 1 for an exit wound.</param>
    /// <param name="hideRegions">Body regions (<see cref="BodyRegions"/> bitmask) whose triangles are not drawn.</param>
    /// <param name="woundStyle">How the wounds look: skin, fabric (hole plus blood soak) or hard (hole only).</param>
    /// <param name="bodyHeat">How warm they show in a thermal scope: 1 alive, less once they're dead.</param>
    public void DrawSkinned(SkinnedModel model, int lod, in float4x4 world, ReadOnlySpan<float4x4> bones, float3 tint,
        ReadOnlySpan<float4> wounds = default, ReadOnlySpan<float4> woundInfo = default,
        uint hideRegions = 0, WoundStyle woundStyle = WoundStyle.Skin, float bodyHeat = 1f)
    {
        _program.Uniform(_uSkin, 2);
        _program.Uniform(Loc("uBodyHeat"), bodyHeat);
        _program.Uniform(_uWoundStyle, (int)woundStyle);
        _program.UniformArray(_uBones, bones.Length > MaxBones ? bones[..MaxBones] : bones);
        var woundCount = int.Min(int.Min(wounds.Length, woundInfo.Length), MaxWounds);
        _program.Uniform(_uWoundCount, woundCount);
        if (woundCount > 0)
        {
            _program.UniformArray(_uWounds, wounds[..woundCount]);
            _program.UniformArray(_uWoundInfo, woundInfo[..woundCount]);
        }
        foreach (var part in model.Parts)
        {
            var m = part.Material;
            PrepareSurface(world, PartColor(m, tint), ShadeMode.Lit, m.Material, new float2(m.Roughness, m.Metalness),
                m.TakesInstanceTint, m.Custom, hasUVs: true);
            part.Draw(lod, hideRegions);
        }
        _program.Uniform(_uSkin, 0);
        _program.Uniform(_uWoundCount, 0);
        _program.Uniform(_uWoundStyle, 0);
    }

    private static float2 DefaultRoughMetal(Material material) => material switch
    {
        Material.Gunmetal => new float2(0.35f, 0.8f),
        Material.Steel or Material.RustyMetal => new float2(0.55f, 0.6f),
        Material.PaintedMetal => new float2(0.5f, 0f),
        Material.Glass => new float2(0.05f, 0f),
        Material.Snow => new float2(0.8f, 0f),
        Material.Carpet => new float2(0.95f, 0f),
        Material.CeilingTile => new float2(0.9f, 0f),
        Material.Veneer => new float2(0.45f, 0f),
        Material.WallPaint => new float2(0.85f, 0f),
        Material.Brick or Material.Whitewash or Material.Grass => new float2(0.9f, 0f),
        Material.Blockwork => new float2(0.6f, 0f),
        Material.RoofSlate => new float2(0.75f, 0f),
        _ => new float2(0.7f, 0f),
    };

    /// <summary>
    /// Switches to <paramref name="material"/>'s texture set (or the model's own <paramref name="custom"/> set).
    /// Returns whether it's textured.
    /// </summary>
    private bool UseMaterial(Material material, TextureSet? custom = null)
    {
        if (material == _boundMaterial && custom == _boundSet)
        {
            _program.Uniform(_uMaterial, (int)material);
            return _boundTextured;
        }

        _boundMaterial = material;
        _boundSet = custom;
        _program.Uniform(_uMaterial, (int)material);
        if (custom is not null)
        {
            Bind(3, custom.Albedo);
            Bind(4, custom.Normal);
            Bind(5, custom.Orm);
            _program.Uniform(_uTextured, 1);
            _program.Uniform(_uTile, custom.Tile);
            _boundTextured = true;
        }
        else if (_textures is not null && _textures.TryGet(material, out var set))
        {
            Bind(3, set.Albedo);
            Bind(4, set.Normal);
            Bind(5, set.Orm);
            _program.Uniform(_uTextured, 1);
            _program.Uniform(_uTile, set.Tile);
            _program.Uniform(_uRefTint, RefTint(material));
            _boundTextured = true;
        }
        else
        {
            _program.Uniform(_uTextured, 0);
            _boundTextured = false;
        }

        return _boundTextured;
    }

    /// <summary>
    /// Draws something lying flat on another surface (e.g. shade on the ground) with a depth bias, so it always wins
    /// the depth test against that surface instead of flickering at a distance.
    /// </summary>
    public void DrawDecal(Mesh mesh, in float4x4 model, float3 color, Material material)
    {
        Enable(GlCapability.PolygonOffsetFill);
        PolygonOffset(-2f, -8f);
        DrawMesh(mesh, model, color, ShadeMode.Lit, material);
        Disable(GlCapability.PolygonOffsetFill);
    }

    /// <summary>
    /// Draws bullet holes (one quad each, see <c>BulletHoles</c>): shaded procedurally per kind, lit like the surface
    /// under them and blended over it with a depth bias, so they never flicker against it.
    /// </summary>
    public void DrawDecals(DynamicMesh decals)
    {
        if (decals.VertexCount == 0)
            return;
        BeginTransparent(additive: false);
        Enable(GlCapability.PolygonOffsetFill);
        PolygonOffset(-2f, -8f);
        _program.Uniform(_uModel, float4x4.Identity);
        _program.Uniform(_uMode, 5);
        _program.Uniform(_uCutout, 0);
        decals.Draw();
        Disable(GlCapability.PolygonOffsetFill);
        EndTransparent();
    }

    /// <summary>
    /// Draws particles (see <c>Particles</c>): <paramref name="lit"/> ones (dust, chips, droplets) are lit and
    /// blended; <paramref name="glowing"/> ones (sparks, tracers, muzzle flash) are added on top. Neither writes depth.
    /// </summary>
    /// <param name="smokeScreen">
    /// The lit ones are a smoke screen's puffs (kind 8, each with its index in its normal's y, under
    /// <see cref="SmokeLightSize"/> squared): hundreds of them, metres across and all overlapping. Lighting every pixel
    /// of every one cost far too much (8 fps in a cloud), so each is lit once, where its centre is, into a texel of a
    /// small texture, and drawn with that.
    /// </param>
    public void DrawParticles(DynamicMesh? lit, DynamicMesh? glowing, bool smokeScreen = false)
    {
        _program.Uniform(_uModel, float4x4.Identity);
        _program.Uniform(_uMode, 6);
        _program.Uniform(_uCutout, 0);
        if (lit is { VertexCount: > 0 })
        {
            BeginTransparent(additive: false);
            if (smokeScreen && _smokeLightReady)
            {
                // Light them: the same quads, each drawn over its own texel. (The texture comes off its unit meanwhile:
                // it mustn't be one the shader could read while it's being drawn into.)
                ActiveTexture(GL_TEXTURE0 + (uint)SmokeLightUnit);
                BindTexture(GL_TEXTURE_2D, default(GlTexture).Id);
                BindFramebuffer(GL_FRAMEBUFFER, _smokeLight.Framebuffer);
                Viewport(0, 0, SmokeLightSize, SmokeLightSize);
                Disable(GlCapability.ScissorTest);
                Disable(GlCapability.DepthTest);
                Disable(GlCapability.Blend);
                _program.Uniform(_uParticleBake, 1);
                lit.Draw();
                // Back to the view, and draw them with it.
                BindFramebuffer(GL_FRAMEBUFFER, _viewTarget?.Framebuffer ?? default);
                Viewport(0, 0, _viewSize.X, _viewSize.Y);
                if (_viewScissored)
                    Enable(GlCapability.ScissorTest);
                Enable(GlCapability.DepthTest);
                Enable(GlCapability.Blend);
                BindTexture(GL_TEXTURE_2D, _smokeLight.Texture.Id);
                ActiveTexture(GL_TEXTURE0);
                _program.Uniform(_uParticleBake, 2);
                lit.Draw();
                _program.Uniform(_uParticleBake, 0);
            }
            else
            {
                lit.Draw();
            }
        }

        if (glowing is { VertexCount: > 0 })
        {
            BeginTransparent(additive: true);
            glowing.Draw();
        }

        EndTransparent();
    }

    /// <summary>Draws clear panes of glass over what's already drawn: both sides show, and they don't write depth.</summary>
    public void DrawGlass(Mesh panes, float3 tint)
    {
        BeginTransparent(additive: false);
        DrawMesh(panes, float4x4.Identity, tint, ShadeMode.Glass);
        EndTransparent();
    }

    /// <summary>Draws a helicopter's blades as a blur (see <c>Chinook</c>): world-space vertices with their own opacity, over what's already drawn.</summary>
    public void DrawRotorBlur(DynamicMesh blur, float3 color)
    {
        if (blur.VertexCount == 0)
            return;
        BeginTransparent(additive: false);
        _program.Uniform(_uModel, float4x4.Identity);
        _program.Uniform(_uColor, color);
        _program.Uniform(_uMode, (int)ShadeMode.RotorBlur);
        _program.Uniform(_uCutout, 0);
        blur.Draw();
        EndTransparent();
    }

    private void BeginTransparent(bool additive)
    {
        Disable(GlCapability.SampleAlphaToCoverage);
        Enable(GlCapability.Blend);
        BlendFunc(GL_SRC_ALPHA, additive ? GL_ONE : GL_ONE_MINUS_SRC_ALPHA);
        DepthMask(false);
        Disable(GlCapability.CullFace);
    }

    private void EndTransparent()
    {
        DepthMask(true);
        Disable(GlCapability.Blend);
        Enable(GlCapability.CullFace);
        Enable(GlCapability.SampleAlphaToCoverage);
    }

    /// <summary>A flat-coloured round portal surface, cut to <paramref name="radius"/> in portal space.</summary>
    public void DrawPortalGlow(in float4x4 model, float3 color, in float4x4 worldToPortal, float radius)
    {
        _program.Uniform(_uPortalWorldToLocal, worldToPortal);
        _program.Uniform(_uPortalRadius, radius);
        // Culling stays on: when looking out of a portal from behind it, its own surface must not block the view.
        DrawCube(model, color, ShadeMode.PortalGlow);
    }

    /// <summary>
    /// Draws a portal opening using the view stored in <paramref name="view"/>, seen through animated water.
    /// <paramref name="ripples"/>: xy = hit point in portal space, z = start time, w = strength.
    /// The box is drawn without face culling so its inside still shows the portal view when the camera
    /// is right up against the opening.
    /// </summary>
    public void DrawPortalSurface(
        in float4x4 model,
        RenderTarget view,
        float3 tint,
        in float4x4 worldToPortal,
        float radius,
        ReadOnlySpan<float4> ripples,
        float time)
    {
        var count = int.Min(ripples.Length, MaxRipples);
        _program.Uniform(_uPortalWorldToLocal, worldToPortal);
        _program.Uniform(_uPortalRadius, radius);
        _program.Uniform(_uRippleCount, count);
        if (count > 0)
            _program.UniformArray(_uRipples, ripples[..count]);
        _program.Uniform(_uTime, time);

        ActiveTexture(GL_TEXTURE0);
        BindTexture(GL_TEXTURE_2D, view.Texture.Id);
        Disable(GlCapability.CullFace);
        DrawCube(model, tint, ShadeMode.PortalView);
        Enable(GlCapability.CullFace);
    }

    /// <summary>
    /// Draws a monitor's screen showing <paramref name="feed"/>: <paramref name="model"/> maps the unit cube onto the
    /// screen, its +z face to the front, x to the right and y up (the picture fills that face). <paramref name="brightness"/>
    /// scales it (1 = as rendered).
    /// </summary>
    /// <summary>
    /// Draws <paramref name="texture"/> unaltered, blended by its alpha, over the unit square <paramref name="model"/> maps
    /// (its +z face; the texture's bottom-left at the square's bottom-left). Call it in the overlay pass.
    /// </summary>
    public void DrawImage(in float4x4 model, GlTexture texture)
    {
        float4x4.Invert(model, out var toLocal);
        _program.Uniform(_uPortalWorldToLocal, toLocal);
        ActiveTexture(GL_TEXTURE0);
        BindTexture(GL_TEXTURE_2D, texture.Id);
        Disable(GlCapability.SampleAlphaToCoverage);
        Enable(GlCapability.Blend);
        BlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
        DrawCube(model, new float3(1f), ShadeMode.Image);
        Disable(GlCapability.Blend);
        Enable(GlCapability.SampleAlphaToCoverage);
    }

    public void DrawScreen(in float4x4 model, RenderTarget feed, float brightness = 1f)
    {
        float4x4.Invert(model, out var toLocal);
        _program.Uniform(_uPortalWorldToLocal, toLocal);
        ActiveTexture(GL_TEXTURE0);
        BindTexture(GL_TEXTURE_2D, feed.Texture.Id);
        DrawCube(model, new float3(brightness), ShadeMode.Screen);
    }

    /// <summary>
    /// Draws a one-way mirror showing <paramref name="reflection"/> (rendered with <see cref="SetMirrored"/>, same size as
    /// the view): <paramref name="model"/> maps the unit cube onto the glass, its +z face to the front. From behind it
    /// draws nothing.
    /// </summary>
    public void DrawMirror(in float4x4 model, RenderTarget reflection, float3 tint)
    {
        float4x4.Invert(model, out var toLocal);
        _program.Uniform(_uPortalWorldToLocal, toLocal);
        ActiveTexture(GL_TEXTURE0);
        BindTexture(GL_TEXTURE_2D, reflection.Texture.Id);
        DrawCube(model, tint, ShadeMode.Mirror);
    }

    /// <summary>
    /// For rendering a mirror's reflection: the view-projection then includes a reflection, which turns every triangle's
    /// winding round, so the front faces are the clockwise ones until this is set back to false.
    /// </summary>
    public void SetMirrored(bool mirrored) => FrontFace((GlFrontFace)(mirrored ? GL_CW : GL_CCW));

    /// <summary>Sets the time used by animated effects (water, dithering).</summary>
    public void SetTime(float time) => _program.Uniform(_uTime, time);

    public void DrawBox(in Aabb box, float3 color, ShadeMode mode = ShadeMode.Lit, Material material = Material.Plain) =>
        DrawCube(float4x4.CreateScale(box.Size) * float4x4.CreateTranslation(box.Center), color, mode, material);

    private readonly Mesh?[] _cubeFaces = new Mesh?[64];

    /// <summary>
    /// Only some faces of a box (bit 0 +x, 1 -x, 2 +y, 3 -y, 4 +z, 5 -z): a wall painted one colour on one side and
    /// another on the other is two of these. The surfaces are mapped in world space, so the faces look just as they
    /// would on the whole box.
    /// </summary>
    public void DrawBoxFaces(in Aabb box, int faces, float3 color, ShadeMode mode = ShadeMode.Lit, Material material = Material.Plain) =>
        DrawCubeFaces(float4x4.CreateScale(box.Size) * float4x4.CreateTranslation(box.Center), faces, color, mode, material);

    /// <summary>Some faces of the unit cube placed by <paramref name="model"/> (the bits as for <see cref="DrawBoxFaces"/>).</summary>
    public void DrawCubeFaces(in float4x4 model, int faces, float3 color, ShadeMode mode = ShadeMode.Lit, Material material = Material.Plain)
    {
        faces &= 63;
        if (faces == 0)
            return;
        var mesh = faces == 63 ? _cube : _cubeFaces[faces] ??= new MeshBuilder().Box(float3.Zero, float3.One, faces).Build();
        DrawMesh(mesh, model, color, mode, material);
    }

    /// <summary>Saves what's in the window's back buffer as a PNG (for screenshots and automated checks).</summary>
    public static unsafe void SaveScreenshot(string path, int2 size, RenderTarget? target = null)
    {
        var stride = size.X * 3;
        var pixels = new byte[stride * size.Y];
        BindFramebuffer(GL_FRAMEBUFFER, target?.Framebuffer ?? default);
        ReadBuffer(target is null ? GL_BACK : GL_COLOR_ATTACHMENT0);
        PixelStorei(GL_PACK_ALIGNMENT, 1);
        fixed (byte* p = pixels)
            ReadPixels(0, 0, size.X, size.Y, GL_RGB, GL_UNSIGNED_BYTE, p);
        BindFramebuffer(GL_FRAMEBUFFER, default);

        // GL's rows start at the bottom; PNG's at the top.
        var flipped = new byte[pixels.Length];
        for (var y = 0; y < size.Y; y++)
            pixels.AsSpan((size.Y - 1 - y) * stride, stride).CopyTo(flipped.AsSpan(y * stride, stride));
        Stb.WritePng(path, size.X, size.Y, 3, flipped, stride);
    }

    public void Dispose()
    {
        _smokeLight.Dispose();
        _program.Dispose();
    }

}

/// <summary>
/// A light in the police station: a ceiling panel (<paramref name="Panel"/>: a flat diffuse emitter facing down) or a
/// point of light (a portal's glow). It lights only inside <paramref name="Bounds"/>, out to <paramref name="Range"/>.
/// </summary>
public readonly record struct OfficeLight(float3 Position, float3 Color, float Intensity, float Range, Aabb Bounds, bool Panel = true);
