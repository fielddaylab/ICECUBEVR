// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Tauri Interactive/Black Hole (Physically)" {
Properties {
	_MainTex ("Base (RGB)", 2D) = "white" {}
}

SubShader {
	Pass {
		ZTest Always Cull Off ZWrite Off
		Fog { Mode off }
				
		CGPROGRAM
		#pragma vertex vert
		#pragma fragment frag
		//#pragma fragmentoption ARB_precision_hint_fastest 
		#include "UnityCG.cginc"

		uniform sampler2D _MainTex;
		uniform float2 _Position;
		uniform float _Rad;
		uniform float _Ratio;
		uniform float _Distance;
		uniform float _DefPowerIN;
		uniform float _DefPowerOUT;
		uniform float _Waves;

		struct v2f {
			float4 pos : POSITION;
			float2 uv : TEXCOORD0;
		};

		v2f vert( appdata_img v )
		{
			v2f o;
			o.pos = UnityObjectToClipPos (v.vertex);
			o.uv = v.texcoord;
			return o;
		}
		
		float4 frag (v2f i) : COLOR
		{
			_DefPowerIN *= 1.05 + _SinTime.z *  _Waves;
			_DefPowerOUT *= 1.05 + _SinTime.z * _Waves;
			float2 offset = i.uv - _Position; 
			float2 ratio = {1,_Ratio};
			ratio.x *= 1.05 + _SinTime.z * _Waves;
			ratio.y *= 1.05 + _SinTime.z * _Waves;
			float rad = length(offset / ratio);

			float deformation = 1/pow(rad*pow(_Distance,_DefPowerIN),_DefPowerOUT)*_Rad*2;
			
			offset =offset*(1-deformation);			
			offset += _Position;			
			half4 res = tex2D(_MainTex, offset);
			if (rad*_Distance<_Rad*1.05)
				res *= pow(rad*_Distance,7);
			return res;
		}
		ENDCG

	}
}

Fallback off

}