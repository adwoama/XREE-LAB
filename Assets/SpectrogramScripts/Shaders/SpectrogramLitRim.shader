Shader "Spectrogram/LitRim"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.0,0.8,0.7,1)
        _RimColor ("Rim Color", Color) = (1,1,1,1)
        _RimPower ("Rim Power", Range(0.5,8)) = 2.5
        _Gloss ("Smoothness", Range(0,1)) = 0.2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _BaseColor;
        fixed4 _RimColor;
        half _RimPower;
        half _Gloss;

        struct Input
        {
            float3 viewDir;
            float3 normal;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 N = normalize(IN.normal);
            float3 V = normalize(IN.viewDir);
            float ndotv = saturate(dot(V, N));
            float rim = pow(1.0 - ndotv, _RimPower);

            o.Albedo = _BaseColor.rgb;
            // add subtle rim
            o.Albedo += _RimColor.rgb * rim * 0.25;
            o.Metallic = 0.0;
            o.Smoothness = _Gloss;
            o.Alpha = _BaseColor.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
