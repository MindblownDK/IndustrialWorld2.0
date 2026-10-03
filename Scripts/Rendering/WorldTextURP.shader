// Assets/Scripts/VoxelEngine/Rendering/WorldTextURP.shader
//
// 14.37.2 - depth-tested world text for TextMesh glyphs.
//
// Unity's stock font shader ("GUI/Text Shader") is built for screen GUI and
// hard-codes ZTest Always, so every 3D TextMesh - banner lines, grid screen
// readouts, rail displays - drew on top of terrain and blocks like an x-ray.
// This is the same glyph-alpha blit with ONE change: ZTest LEqual, so world
// text sits in the world and is occluded like everything else.
//
// Applied at runtime by WorldTextMaterial.Apply(TextMesh). Vertex colors
// carry the per-text tint (TextMesh bakes its color into the mesh), so one
// shared material serves every text that uses the same font atlas.

Shader "VoxelEngine/WorldText"
{
    Properties
    {
        _MainTex ("Font Texture", 2D) = "white" {}
        _Color ("Text Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
        }

        Lighting Off
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Font atlases are alpha-only - the glyph lives in .a, the
                // tint in the vertex color.
                fixed4 col = i.color;
                col.a *= tex2D(_MainTex, i.texcoord).a;
                return col;
            }
            ENDCG
        }
    }
}
