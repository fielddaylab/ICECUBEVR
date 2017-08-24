Shader "Unlit/AlphaShader"
{
  Properties
  {
  }
  SubShader
  {
    Tags { "Queue"="Transparent+2" "RenderType"="Transparent" "IgnoreProjector"="True" }

    Pass
    {
      Cull back
      Blend SrcAlpha OneMinusSrcAlpha
      ZWrite Off
      ZTest Always

      CGPROGRAM
      #pragma vertex vert
      #pragma fragment frag
      #include "UnityCG.cginc"

      float alpha;

      struct appdata
      {
        float4 vertex : POSITION;
      };

      struct v2f
      {
        float4 vertex : SV_POSITION;
      };

      v2f vert (appdata v)
      {
        v2f o;
        o.vertex = UnityObjectToClipPos(v.vertex);
        return o;
      }

      fixed4 frag (v2f i) : SV_Target
      {
        return float4(1,1,1,alpha);
      }
      ENDCG
    }
  }
}
