Shader "Spectrogram/SimpleUnlitRim"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.0,0.8,0.7,1)
        _RimColor ("Rim Color", Color) = (1,1,1,1)
        _RimPower ("Rim Power", Range(0.5,8)) = 2.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            struct appdata {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
            };

            fixed4 _BaseColor;
            fixed4 _RimColor;
            half _RimPower;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                // transform normal to world space
                o.normal = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDir = _WorldSpaceCameraPos - worldPos;
                return o;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float3 N = normalize(IN.normal);
                float3 V = normalize(IN.viewDir);
                float ndotv = saturate(dot(V, N));
                float rim = pow(1.0 - ndotv, _RimPower);
                fixed3 col = _BaseColor.rgb + _RimColor.rgb * rim * 0.35;
                return fixed4(col, _BaseColor.a);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
