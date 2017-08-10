Shader "Unlit/PortalShader"
{
  Properties
  {
    _MainTex ("Texture", 2D) = "white" {}
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

      #include "UnityCG.cginc"

      struct appdata
      {
        float4 vertex : POSITION;
      };

      struct v2f
      {
        float2 uv : TEXCOORD0;
        float vw : TEXCOORD1;
        float4 vertex : SV_POSITION;
      };

      sampler2D _MainTex;
      float4 _MainTex_ST;

      v2f vert (appdata v)
      {
        v2f o;
        o.vertex = UnityObjectToClipPos(v.vertex);
        o.uv = ComputeScreenPos(o.vertex);
        o.vw = o.vertex.w;
        return o;
      }

      fixed4 frag (v2f i) : SV_Target
      {
        fixed4 col = tex2D(_MainTex, i.uv/i.vw);
        return col;
      }
      ENDCG
    }
  }
}
