Shader "Spectrogram/HeightDisplace"
{
    Properties
    {
        _HeightTex ("Height Texture", 2D) = "white" {}
        _FrameNormalized ("Frame", Range(0,1)) = 0
        _FreqCoord ("FreqCoord", Float) = 0
        _HeightScale ("Height Scale", Float) = 1
        _Color ("Color", Color) = (1,0,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _HeightTex;
            float _FrameNormalized;
            float _FreqCoord;
            float _HeightScale;
            float4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float4 pos = v.vertex;
                o.pos = UnityObjectToClipPos(pos);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Sample height for coloring only (no vertex displacement in shader)
                float2 sampleUV = float2(_FreqCoord, _FrameNormalized);
                float h = tex2D(_HeightTex, sampleUV).r;
                return lerp(float4(0.2,0.0,0.8,1.0), _Color, h);
            }
            ENDCG
        }
    }
}
