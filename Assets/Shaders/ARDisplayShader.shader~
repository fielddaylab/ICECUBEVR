Shader "Unlit/ARDisplayShader"
{
  Properties
  {
    _MainTex ("Texture", 2D) = "white" {}
  }
  SubShader
  {
    Tags { "Queue"="Transparent+1" "RenderType"="Transparent" "IgnoreProjector"="True" }
    LOD 100

    Pass
    {
      Cull back
      Blend SrcAlpha OneMinusSrcAlpha
      ZTest NotEqual

      CGPROGRAM
      #pragma vertex vert
      #pragma fragment frag
      #include "UnityCG.cginc"

      float time_mod_twelve_pi;
      float jitter;

      float rand(float3 co)
      {
        return frac(sin( dot(co.xyz ,float3(time_mod_twelve_pi,78.233,45.5432) )) * 43758.5453);
      }

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
        float y = i.uv.y/i.vw;
        float a = 1.;//0.6+sin(y*4000)*0.2;
        float rx = rand(float3(0.2+i.uv.y,0.1+i.uv.y,0.4));
        float rt = rand(float3(0.2+i.uv.x,0.3+i.uv.y,0.5));
        float2 uv = float2(i.uv.x + rx*jitter*0.4 + jitter*i.uv.y*0.25, i.uv.y)/i.vw;
        float glow_a = 0.;
        float off = 0.002;
        glow_a += tex2D(_MainTex,uv+fixed2(   0, off)).a;
        glow_a += tex2D(_MainTex,uv+fixed2(   0,-off)).a;
        glow_a += tex2D(_MainTex,uv+fixed2( off,   0)).a;
        glow_a += tex2D(_MainTex,uv+fixed2(-off,   0)).a;
        glow_a += tex2D(_MainTex,uv+fixed2( off, off)).a;
        glow_a += tex2D(_MainTex,uv+fixed2(-off, off)).a;
        glow_a += tex2D(_MainTex,uv+fixed2( off,-off)).a;
        glow_a += tex2D(_MainTex,uv+fixed2(-off,-off)).a;
        glow_a /= 8.;
        glow_a /= 1.;
        fixed4 glow = fixed4(220./255.,255./255.,255./255.,glow_a);
        fixed4 col = fixed4(198./255.,254./255.,254./255.,a*tex2D(_MainTex, uv).a+(jitter*rt));
        //col.r = (col.r*col.a+(1.-col.a)*(glow.r*glow.a));
        //col.g = (col.g*col.a+(1.-col.a)*(glow.g*glow.a));
        //col.b = (col.b*col.a+(1.-col.a)*(glow.b*glow.a));
        col.a = (      col.a+(1.-col.a)*(       glow.a));
        return col;
      }
      ENDCG
    }
  }
}

