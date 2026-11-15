Shader "CrystalGourd/Lit" {
Properties {
[HideInInspector] _SrcBlend("Source blend",Float)=1
[HideInInspector] _DstBlend("Destination blend",Float)=0
[HideInInspector] _ZWrite("Write depth",Float)=1
[HideInInspector] _Cull("Cull",Float)=0
_Opacity("Face opacity",Range(0,1))=1
_Glow("Glow",Range(0,3))=0 _TextureLift("Texture lift",Range(0,1))=0 _Specular("Specular",Range(0,1))=0 _Shininess("Shininess",Range(1,128))=24 _CrystalSaturation("Saturation",Range(0,1))=1  _MainTex("Texture",2D)="white"{} _Color("Tint",Color)=(1,1,1,1) _Cutoff("Cutoff",Range(0,1))=0 _UseAtlas("Atlas",Float)=1 _UseVertexColor("Vertex color",Float)=0 }
SubShader { Tags {"RenderType"="Opaque"} Cull [_Cull]
Pass { Tags {"LightMode"="ForwardBase"} Blend [_SrcBlend] [_DstBlend] ZWrite [_ZWrite]
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma target 3.5
#pragma multi_compile __ CRYSTAL_GOURD_STEREO
#pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
#pragma multi_compile_instancing
#pragma multi_compile_fwdbase
#include "PlayerArmorStereo.cginc"
#include "Lighting.cginc"
#include "AutoLight.cginc"
float4 _CrystalGourdAmbient;float _CrystalGourdAmbientActive;
sampler2D _MainTex;float4 _MainTex_ST;fixed4 _Color;float _Cutoff,_UseAtlas,_UseVertexColor,_Glow,_CrystalSaturation,_Specular,_Shininess,_TextureLift,_Opacity;
struct appdata {float4 vertex:POSITION;float3 normal:NORMAL;float2 uv:TEXCOORD0;float4 atlas:TEXCOORD3;fixed4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
struct v2f {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float4 atlas:TEXCOORD1;float3 normal:TEXCOORD2;fixed4 color:COLOR;float3 worldPos:TEXCOORD4;SHADOW_COORDS(5) float3 vertexLights:TEXCOORD6; UNITY_VERTEX_OUTPUT_STEREO};
v2f vert(appdata v){v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_OUTPUT(v2f,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.pos=UnityObjectToClipPos(v.vertex);o.uv=TRANSFORM_TEX(v.uv,_MainTex);o.atlas=v.atlas;o.color=v.color;o.normal=UnityObjectToWorldNormal(v.normal);o.worldPos=mul(unity_ObjectToWorld,v.vertex).xyz;o.vertexLights=0;
#ifdef VERTEXLIGHT_ON
o.vertexLights=Shade4PointLights(unity_4LightPosX0,unity_4LightPosY0,unity_4LightPosZ0,unity_LightColor[0].rgb,unity_LightColor[1].rgb,unity_LightColor[2].rgb,unity_LightColor[3].rgb,unity_4LightAtten0,o.worldPos,normalize(o.normal));
#endif
TRANSFER_SHADOW(o);return o;}
fixed4 frag(v2f i):SV_Target {UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);float2 uv=i.uv;if(_UseAtlas>.5&&i.atlas.z>0&&i.atlas.w>0)uv=i.atlas.xy+frac(uv)*i.atlas.zw;fixed4 c=tex2D(_MainTex,uv);c.rgb=lerp(dot(c.rgb,float3(.25,.60,.15)).xxx,c.rgb,_CrystalSaturation);c.rgb=lerp(c.rgb,float3(1,.73,.57),_TextureLift);c*=_Color*lerp(fixed4(1,1,1,1),i.color,_UseVertexColor);clip(c.a-_Cutoff);float3 n=normalize(i.normal);UNITY_LIGHT_ATTENUATION(atten,i,i.worldPos);float3 ambient=_CrystalGourdAmbientActive>.5?_CrystalGourdAmbient.rgb:UNITY_LIGHTMODEL_AMBIENT.rgb*.5;float3 lighting=max(ambient,0)+i.vertexLights+_LightColor0.rgb*saturate(dot(n,normalize(UnityWorldSpaceLightDir(i.worldPos))))*atten;float3 lightDir=normalize(UnityWorldSpaceLightDir(i.worldPos));float3 view=normalize(UnityWorldSpaceViewDir(i.worldPos));float3 halfway=normalize(lightDir+view);float sheen=_Specular*pow(saturate(dot(n,halfway)),_Shininess)*step(0.001,dot(n,lightDir))*atten;float opacity=lerp(_Opacity,1,pow(1-saturate(dot(n,view)),3));return fixed4(c.rgb*(lighting+_Glow)+_LightColor0.rgb*sheen,opacity);}
ENDHLSL
}
Pass { Tags {"LightMode"="ForwardAdd"} Blend One One ZWrite Off
HLSLPROGRAM
#pragma vertex vertAdd
#pragma fragment fragAdd
#pragma target 3.5
#pragma multi_compile __ CRYSTAL_GOURD_STEREO
#pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
#pragma multi_compile_instancing
#pragma multi_compile_fwdadd_fullshadows
#include "PlayerArmorStereo.cginc"
#include "Lighting.cginc"
#include "AutoLight.cginc"
sampler2D _MainTex;float4 _MainTex_ST;fixed4 _Color;float _Cutoff,_UseAtlas,_UseVertexColor,_Glow,_CrystalSaturation,_Specular,_Shininess,_TextureLift,_Opacity;
struct appdataAdd {float4 vertex:POSITION;float3 normal:NORMAL;float2 uv:TEXCOORD0;float4 atlas:TEXCOORD3;fixed4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
struct v2fAdd {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float4 atlas:TEXCOORD1;float3 normal:TEXCOORD2;fixed4 color:COLOR;float3 worldPos:TEXCOORD4;LIGHTING_COORDS(5,6) UNITY_VERTEX_OUTPUT_STEREO};
v2fAdd vertAdd(appdataAdd v){v2fAdd o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_OUTPUT(v2fAdd,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.pos=UnityObjectToClipPos(v.vertex);o.uv=TRANSFORM_TEX(v.uv,_MainTex);o.atlas=v.atlas;o.color=v.color;o.normal=UnityObjectToWorldNormal(v.normal);o.worldPos=mul(unity_ObjectToWorld,v.vertex).xyz;TRANSFER_VERTEX_TO_FRAGMENT(o);return o;}
fixed4 fragAdd(v2fAdd i):SV_Target {UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);float2 uv=i.uv;if(_UseAtlas>.5&&i.atlas.z>0&&i.atlas.w>0)uv=i.atlas.xy+frac(uv)*i.atlas.zw;fixed4 c=tex2D(_MainTex,uv);c.rgb=lerp(dot(c.rgb,float3(.25,.60,.15)).xxx,c.rgb,_CrystalSaturation);c.rgb=lerp(c.rgb,float3(1,.73,.57),_TextureLift);c*=_Color*lerp(fixed4(1,1,1,1),i.color,_UseVertexColor);clip(c.a-_Cutoff);float3 n=normalize(i.normal);UNITY_LIGHT_ATTENUATION(atten,i,i.worldPos);float3 lightDir=normalize(UnityWorldSpaceLightDir(i.worldPos));float3 view=normalize(UnityWorldSpaceViewDir(i.worldPos));float3 halfway=normalize(lightDir+view);float sheen=_Specular*pow(saturate(dot(n,halfway)),_Shininess)*step(0.001,dot(n,lightDir));float opacity=lerp(_Opacity,1,pow(1-saturate(dot(n,view)),3));return fixed4((c.rgb*saturate(dot(n,lightDir))+sheen)*_LightColor0.rgb*atten*opacity,0);}
ENDHLSL
}
UsePass "Legacy Shaders/VertexLit/SHADOWCASTER"
} Fallback "Diffuse"
}
