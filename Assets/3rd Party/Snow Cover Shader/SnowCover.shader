// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'
// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

// Shader created with Shader Forge v1.18 
// Shader Forge (c) Neat Corporation / Joachim Holmer - http://www.acegikmo.com/shaderforge/
// Note: Manually altering this data may prevent you from opening it in Shader Forge
/*SF_DATA;ver:1.18;sub:START;pass:START;ps:flbk:,iptp:0,cusa:False,bamd:0,lico:1,lgpr:1,limd:1,spmd:1,trmd:0,grmd:0,uamb:True,mssp:True,bkdf:False,hqlp:False,rprd:False,enco:False,rmgx:True,rpth:0,hqsc:True,nrmq:1,nrsp:0,vomd:0,spxs:False,tesm:0,culm:0,bsrc:0,bdst:1,dpts:2,wrdp:True,dith:0,rfrpo:True,rfrpn:Refraction,coma:15,ufog:False,aust:True,igpj:False,qofs:0,qpre:1,rntp:1,fgom:False,fgoc:False,fgod:False,fgor:False,fgmd:0,fgcr:0.5,fgcg:0.5,fgcb:0.5,fgca:1,fgde:0.01,fgrn:0,fgrf:300,stcl:False,stva:128,stmr:255,stmw:255,stcp:6,stps:0,stfa:0,stfz:0,ofsf:0,ofsu:0,f2p0:False;n:type:ShaderForge.SFN_Final,id:3138,x:34031,y:32965,varname:node_3138,prsc:2|diff-7063-OUT,normal-6275-OUT;n:type:ShaderForge.SFN_Color,id:7241,x:33152,y:33104,ptovrint:False,ptlb:SnowColor,ptin:_SnowColor,varname:node_7241,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,c1:1,c2:1,c3:1,c4:1;n:type:ShaderForge.SFN_Tex2d,id:3099,x:32324,y:33196,ptovrint:False,ptlb:MainTex,ptin:_MainTex,varname:node_3099,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,tex:4455ea74c30038a4c9d33fcc02999a6a,ntxv:0,isnm:False;n:type:ShaderForge.SFN_Tex2d,id:6355,x:32324,y:33430,ptovrint:False,ptlb:BumpMap,ptin:_BumpMap,varname:node_6355,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,tex:950a5f7efd50b464f8d5b0a9d508a733,ntxv:3,isnm:True;n:type:ShaderForge.SFN_Tex2d,id:4886,x:32416,y:33692,ptovrint:False,ptlb:snowNormal,ptin:_snowNormal,varname:node_4886,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,tex:4b1853d3b354a6441bdb6d713912cef0,ntxv:3,isnm:True|UVIN-7611-OUT;n:type:ShaderForge.SFN_Lerp,id:6275,x:32703,y:33593,varname:node_6275,prsc:2|A-6355-RGB,B-4886-RGB,T-3099-RGB;n:type:ShaderForge.SFN_Multiply,id:7611,x:32197,y:33859,varname:node_7611,prsc:2|A-9160-UVOUT,B-1396-OUT;n:type:ShaderForge.SFN_TexCoord,id:9160,x:31955,y:33695,varname:node_9160,prsc:2,uv:0;n:type:ShaderForge.SFN_Vector1,id:1396,x:31893,y:33907,varname:node_1396,prsc:2,v1:5;n:type:ShaderForge.SFN_Multiply,id:7670,x:32409,y:32863,varname:node_7670,prsc:2|A-8754-OUT,B-3099-B;n:type:ShaderForge.SFN_Slider,id:2801,x:31736,y:32698,ptovrint:False,ptlb:SnowAmount,ptin:_SnowAmount,varname:node_2801,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,min:0,cur:0,max:20;n:type:ShaderForge.SFN_Multiply,id:9589,x:32409,y:32607,varname:node_9589,prsc:2|A-757-OUT,B-7670-OUT;n:type:ShaderForge.SFN_OneMinus,id:978,x:32617,y:32673,varname:node_978,prsc:2|IN-9589-OUT;n:type:ShaderForge.SFN_Add,id:6040,x:32835,y:33007,varname:node_6040,prsc:2|A-978-OUT,B-8754-OUT;n:type:ShaderForge.SFN_Power,id:7951,x:32923,y:32662,varname:node_7951,prsc:2|VAL-6040-OUT,EXP-3784-OUT;n:type:ShaderForge.SFN_Vector1,id:3784,x:32866,y:32798,varname:node_3784,prsc:2,v1:1;n:type:ShaderForge.SFN_ConstantClamp,id:1018,x:33130,y:32787,varname:node_1018,prsc:2,min:0,max:1|IN-7951-OUT;n:type:ShaderForge.SFN_Lerp,id:7063,x:33618,y:32873,varname:node_7063,prsc:2|A-3099-RGB,B-5890-OUT,T-1018-OUT;n:type:ShaderForge.SFN_Subtract,id:757,x:32089,y:32629,varname:node_757,prsc:2|A-6287-OUT,B-2801-OUT;n:type:ShaderForge.SFN_Vector1,id:6287,x:31880,y:32557,varname:node_6287,prsc:2,v1:20;n:type:ShaderForge.SFN_Vector1,id:8754,x:32029,y:32827,varname:node_8754,prsc:2,v1:1;n:type:ShaderForge.SFN_Multiply,id:5890,x:33349,y:32694,varname:node_5890,prsc:2|A-1018-OUT,B-7241-RGB;proporder:3099-6355-7241-4886-2801;pass:END;sub:END;*/

Shader "3y3net/SnowCover" {
    Properties {
        _MainTex ("MainTex", 2D) = "white" {}
        _BumpMap ("BumpMap", 2D) = "bump" {}
        _SnowColor ("SnowColor", Color) = (1,1,1,1)
        _snowNormal ("snowNormal", 2D) = "bump" {}
        _SnowAmount ("SnowAmount", Range(0, 20)) = 0
    }
    SubShader {
        Tags {
            "RenderType"="Opaque"
        }
        Pass {
            Name "FORWARD"
            Tags {
                "LightMode"="ForwardBase"
            }
            
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define UNITY_PASS_FORWARDBASE
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"
            #pragma multi_compile_fwdbase_fullshadows
            #pragma exclude_renderers gles3 metal d3d11_9x xbox360 xboxone ps3 ps4 psp2 
            #pragma target 3.0
            uniform float4 _LightColor0;
            uniform float4 _SnowColor;
            uniform sampler2D _MainTex; uniform float4 _MainTex_ST;
            uniform sampler2D _BumpMap; uniform float4 _BumpMap_ST;
            uniform sampler2D _snowNormal; uniform float4 _snowNormal_ST;
            uniform float _SnowAmount;
            struct VertexInput {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 texcoord0 : TEXCOORD0;
            };
            struct VertexOutput {
                float4 pos : SV_POSITION;
                float2 uv0 : TEXCOORD0;
                float4 posWorld : TEXCOORD1;
                float3 normalDir : TEXCOORD2;
                float3 tangentDir : TEXCOORD3;
                float3 bitangentDir : TEXCOORD4;
                LIGHTING_COORDS(5,6)
            };
            VertexOutput vert (VertexInput v) {
                VertexOutput o = (VertexOutput)0;
                o.uv0 = v.texcoord0;
                o.normalDir = UnityObjectToWorldNormal(v.normal);
                o.tangentDir = normalize( mul( unity_ObjectToWorld, float4( v.tangent.xyz, 0.0 ) ).xyz );
                o.bitangentDir = normalize(cross(o.normalDir, o.tangentDir) * v.tangent.w);
                o.posWorld = mul(unity_ObjectToWorld, v.vertex);
                float3 lightColor = _LightColor0.rgb;
                o.pos = UnityObjectToClipPos(v.vertex);
                TRANSFER_VERTEX_TO_FRAGMENT(o)
                return o;
            }
            float4 frag(VertexOutput i) : COLOR {
                i.normalDir = normalize(i.normalDir);
                float3x3 tangentTransform = float3x3( i.tangentDir, i.bitangentDir, i.normalDir);
/////// Vectors:
                float3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - i.posWorld.xyz);
                float3 _BumpMap_var = UnpackNormal(tex2D(_BumpMap,TRANSFORM_TEX(i.uv0, _BumpMap)));
                float2 node_7611 = (i.uv0*5.0);
                float3 _snowNormal_var = UnpackNormal(tex2D(_snowNormal,TRANSFORM_TEX(node_7611, _snowNormal)));
                float4 _MainTex_var = tex2D(_MainTex,TRANSFORM_TEX(i.uv0, _MainTex));
                float3 normalLocal = lerp(_BumpMap_var.rgb,_snowNormal_var.rgb,_MainTex_var.rgb);
                float3 normalDirection = normalize(mul( normalLocal, tangentTransform )); // Perturbed normals
                float3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
                float3 lightColor = _LightColor0.rgb;
////// Lighting:
                float attenuation = LIGHT_ATTENUATION(i);
                float3 attenColor = attenuation * _LightColor0.xyz;
/////// Diffuse:
                float NdotL = max(0.0,dot( normalDirection, lightDirection ));
                float3 directDiffuse = max( 0.0, NdotL) * attenColor;
                float3 indirectDiffuse = float3(0,0,0);
                indirectDiffuse += UNITY_LIGHTMODEL_AMBIENT.rgb; // Ambient Light
                float node_8754 = 1.0;
                float node_1018 = clamp(pow(((1.0 - ((20.0-_SnowAmount)*(node_8754*_MainTex_var.b)))+node_8754),1.0),0,1);
                float3 diffuseColor = lerp(_MainTex_var.rgb,(node_1018*_SnowColor.rgb),node_1018);
                float3 diffuse = (directDiffuse + indirectDiffuse) * diffuseColor;
/// Final Color:
                float3 finalColor = diffuse;
                return fixed4(finalColor,1);
            }
            ENDCG
        }
        Pass {
            Name "FORWARD_DELTA"
            Tags {
                "LightMode"="ForwardAdd"
            }
            Blend One One
            
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define UNITY_PASS_FORWARDADD
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"
            #pragma multi_compile_fwdadd_fullshadows
            #pragma exclude_renderers gles3 metal d3d11_9x xbox360 xboxone ps3 ps4 psp2 
            #pragma target 3.0
            uniform float4 _LightColor0;
            uniform float4 _SnowColor;
            uniform sampler2D _MainTex; uniform float4 _MainTex_ST;
            uniform sampler2D _BumpMap; uniform float4 _BumpMap_ST;
            uniform sampler2D _snowNormal; uniform float4 _snowNormal_ST;
            uniform float _SnowAmount;
            struct VertexInput {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 texcoord0 : TEXCOORD0;
            };
            struct VertexOutput {
                float4 pos : SV_POSITION;
                float2 uv0 : TEXCOORD0;
                float4 posWorld : TEXCOORD1;
                float3 normalDir : TEXCOORD2;
                float3 tangentDir : TEXCOORD3;
                float3 bitangentDir : TEXCOORD4;
                LIGHTING_COORDS(5,6)
            };
            VertexOutput vert (VertexInput v) {
                VertexOutput o = (VertexOutput)0;
                o.uv0 = v.texcoord0;
                o.normalDir = UnityObjectToWorldNormal(v.normal);
                o.tangentDir = normalize( mul( unity_ObjectToWorld, float4( v.tangent.xyz, 0.0 ) ).xyz );
                o.bitangentDir = normalize(cross(o.normalDir, o.tangentDir) * v.tangent.w);
                o.posWorld = mul(unity_ObjectToWorld, v.vertex);
                float3 lightColor = _LightColor0.rgb;
                o.pos = UnityObjectToClipPos(v.vertex);
                TRANSFER_VERTEX_TO_FRAGMENT(o)
                return o;
            }
            float4 frag(VertexOutput i) : COLOR {
                i.normalDir = normalize(i.normalDir);
                float3x3 tangentTransform = float3x3( i.tangentDir, i.bitangentDir, i.normalDir);
/////// Vectors:
                float3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - i.posWorld.xyz);
                float3 _BumpMap_var = UnpackNormal(tex2D(_BumpMap,TRANSFORM_TEX(i.uv0, _BumpMap)));
                float2 node_7611 = (i.uv0*5.0);
                float3 _snowNormal_var = UnpackNormal(tex2D(_snowNormal,TRANSFORM_TEX(node_7611, _snowNormal)));
                float4 _MainTex_var = tex2D(_MainTex,TRANSFORM_TEX(i.uv0, _MainTex));
                float3 normalLocal = lerp(_BumpMap_var.rgb,_snowNormal_var.rgb,_MainTex_var.rgb);
                float3 normalDirection = normalize(mul( normalLocal, tangentTransform )); // Perturbed normals
                float3 lightDirection = normalize(lerp(_WorldSpaceLightPos0.xyz, _WorldSpaceLightPos0.xyz - i.posWorld.xyz,_WorldSpaceLightPos0.w));
                float3 lightColor = _LightColor0.rgb;
////// Lighting:
                float attenuation = LIGHT_ATTENUATION(i);
                float3 attenColor = attenuation * _LightColor0.xyz;
/////// Diffuse:
                float NdotL = max(0.0,dot( normalDirection, lightDirection ));
                float3 directDiffuse = max( 0.0, NdotL) * attenColor;
                float node_8754 = 1.0;
                float node_1018 = clamp(pow(((1.0 - ((20.0-_SnowAmount)*(node_8754*_MainTex_var.b)))+node_8754),1.0),0,1);
                float3 diffuseColor = lerp(_MainTex_var.rgb,(node_1018*_SnowColor.rgb),node_1018);
                float3 diffuse = directDiffuse * diffuseColor;
/// Final Color:
                float3 finalColor = diffuse;
                return fixed4(finalColor * 1,0);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
    CustomEditor "ShaderForgeMaterialInspector"
}
