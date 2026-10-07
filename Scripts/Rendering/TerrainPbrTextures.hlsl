#ifndef TERRAIN_PBR_TEXTURES_INCLUDED
#define TERRAIN_PBR_TEXTURES_INCLUDED
TEXTURE2D_ARRAY(_TerrainPbrAlbedo); SAMPLER(sampler_TerrainPbrAlbedo);
TEXTURE2D_ARRAY(_TerrainPbrNormal); SAMPLER(sampler_TerrainPbrNormal);
TEXTURE2D_ARRAY(_TerrainPbrMask); SAMPLER(sampler_TerrainPbrMask);
float4 _TerrainPbrEntries[256];
float _TerrainPbrReady;

void TerrainPbr(float matId, float3 coord, inout float3 normalWS, inout float3 albedo,
    inout float smoothness, inout float metallic, inout float occlusion)
{
    if (_TerrainPbrReady < 0.5) return;
    float4 entry = _TerrainPbrEntries[(int)clamp(matId, 0.0, 255.0)];
    if (entry.x < 0.5) return; // Named albedo absent: exact legacy branch.
    float slice = entry.x - 1.0;
    float3 p = coord / max(entry.w, 0.05);
    float3 weights = pow(abs(normalWS), 4.0);
    weights /= max(dot(weights, float3(1,1,1)), 0.0001);
    float3 signs = lerp(-1.0, 1.0, step(0.0, normalWS));
    float2 xuv = float2(p.z * -signs.x, p.y);
    float2 yuv = float2(p.x * signs.y, p.z);
    float2 zuv = float2(p.x * signs.z, p.y);
    float3 ax = SAMPLE_TEXTURE2D_ARRAY(_TerrainPbrAlbedo, sampler_TerrainPbrAlbedo, xuv, slice).rgb;
    float3 ay = SAMPLE_TEXTURE2D_ARRAY(_TerrainPbrAlbedo, sampler_TerrainPbrAlbedo, yuv, slice).rgb;
    float3 az = SAMPLE_TEXTURE2D_ARRAY(_TerrainPbrAlbedo, sampler_TerrainPbrAlbedo, zuv, slice).rgb;
    albedo = ax * weights.x + ay * weights.y + az * weights.z;
    if (entry.z > 0.5)
    {
        float3 mx = SAMPLE_TEXTURE2D_ARRAY(_TerrainPbrMask, sampler_TerrainPbrMask, xuv, slice).rgb;
        float3 my = SAMPLE_TEXTURE2D_ARRAY(_TerrainPbrMask, sampler_TerrainPbrMask, yuv, slice).rgb;
        float3 mz = SAMPLE_TEXTURE2D_ARRAY(_TerrainPbrMask, sampler_TerrainPbrMask, zuv, slice).rgb;
        float3 mask = mx * weights.x + my * weights.y + mz * weights.z;
        metallic = mask.r; occlusion = mask.g; smoothness = 1.0 - mask.b;
    }
    if (entry.y > 0.5)
    {
        // Raw RGB OpenGL tangent normals, not Unity's platform-dependent compressed encoding.
        float3 nx = SAMPLE_TEXTURE2D_ARRAY(_TerrainPbrNormal, sampler_TerrainPbrNormal, xuv, slice).xyz * 2.0 - 1.0;
        float3 ny = SAMPLE_TEXTURE2D_ARRAY(_TerrainPbrNormal, sampler_TerrainPbrNormal, yuv, slice).xyz * 2.0 - 1.0;
        float3 nz = SAMPLE_TEXTURE2D_ARRAY(_TerrainPbrNormal, sampler_TerrainPbrNormal, zuv, slice).xyz * 2.0 - 1.0;
        float3 bump = float3(0,nx.y,-nx.x*signs.x)*weights.x
            + float3(ny.x*signs.y,0,ny.y)*weights.y
            + float3(nz.x*signs.z,nz.y,0)*weights.z;
        normalWS = normalize(normalWS + bump);
    }
}
#endif
