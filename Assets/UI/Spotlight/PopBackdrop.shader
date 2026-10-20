Shader "TapTap/UI/Pop Backdrop"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _BaseA ("Base A", Color) = (0.94, 0.94, 1, 1)
        _BaseB ("Base B", Color) = (1, 0.97, 0.99, 1)
        _Blue ("Electric Blue", Color) = (0.05, 0.04, 1, 1)
        _Pink ("Hot Pink", Color) = (1, 0.19, 0.52, 1)
        _Accent ("Accent Strength", Range(0,1)) = 0.3
        _Hero ("Hero Composition", Range(0,1)) = 0
        _Dark ("Dark Stage", Range(0,1)) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ColorMask [_ColorMask]
        Pass
        {
            Name "PopBackdrop"
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float3 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseA, _BaseB, _Blue, _Pink;
                float _Accent, _Hero, _Dark;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS=TransformObjectToHClip(input.positionOS);
                output.uv=input.uv; output.color=input.color; return output;
            }
            float Band(float distance, float width, float aa)
            { return 1-smoothstep(width, width+aa, abs(distance)); }
            float Frame(float2 p, float2 extents, float width, float aa)
            { return Band(max(abs(p.x)-extents.x, abs(p.y)-extents.y), width, aa); }
            float2 Rotate(float2 p, float angle)
            { float s=sin(angle), c=cos(angle); return float2(c*p.x-s*p.y, s*p.x+c*p.y); }
            half4 Frag(Varyings input):SV_Target
            {
                float2 uv=input.uv;
                float aa=max(fwidth(uv.x),fwidth(uv.y))*1.5;
                float aspect=_ScreenParams.x/max(1,_ScreenParams.y);
                float2 p=float2((uv.x-.5)*aspect, uv.y-.5);
                half3 color=lerp(_BaseA.rgb,_BaseB.rgb,saturate(uv.x*.65+uv.y*.35));
                float glow=exp(-dot(p-float2(.35,.18),p-float2(.35,.18))*3);
                color+=lerp(half3(.015,.005,.01),half3(.015,.008,.055),_Dark)*glow;

                // Keep the central play/content area quiet; marks gather at the page edges.
                float edge=uv.x+.30*uv.y;
                float ribbon=smoothstep(1.05,1.05+aa,edge);
                float lower=1-smoothstep(.12,.12+aa,edge);
                color=lerp(color,_Blue.rgb,ribbon*_Accent*lerp(.18,.23,_Dark));
                color=lerp(color,_Pink.rgb,lower*_Accent*.22);
                color=lerp(color,_Blue.rgb,Band(edge-1.14,.005,aa)*_Accent);
                color=lerp(color,_Pink.rgb,Band(edge-1.18,.009,aa)*_Accent);
                color=lerp(color,_Blue.rgb,Band(edge-.05,.007,aa)*_Accent*.85);

                float2 grid=uv*float2(92*aspect,92);
                float dots=1-smoothstep(.11,.16,length(frac(grid)-.5));
                float dotArea=saturate((uv.x-.72)*4)+saturate((.17-uv.y)*5);
                color=lerp(color,lerp(_Blue.rgb,_Pink.rgb,uv.x),dots*saturate(dotArea)*_Accent*.19);
                float slash=Band(uv.x+.42*uv.y-.95,.0015,aa);
                color=lerp(color,_Pink.rgb,slash*_Accent*.25);

                float2 center=float2(.47*aspect,.05);
                float2 framePoint=p-center;
                float blueFrame=Frame(Rotate(framePoint,.16),float2(.27*aspect,.27),.007,aa);
                float pinkFrame=Frame(Rotate(framePoint,-.095),float2(.25*aspect,.30),.008,aa);
                float filled=1-smoothstep(0,aa,max(abs(Rotate(framePoint,.16).x)-.26*aspect,abs(Rotate(framePoint,.16).y)-.26));
                color=lerp(color,lerp(_Blue.rgb,_BaseB.rgb,.9),filled*_Hero*.65);
                color=lerp(color,_Blue.rgb,blueFrame*_Hero*.85);
                color=lerp(color,_Pink.rgb,pinkFrame*_Hero*.9);

                // A restrained equalizer motif echoes the rhythm game and the slanted UI cards.
                float2 bars=uv-float2(.79,.205);
                float column=floor(bars.x*100);
                float height=.018+.026*(.5+.5*sin(column*1.7));
                float bar=step(0,bars.x)*step(bars.x,.13)*step(0,bars.y)*step(bars.y,height)*step(frac(bars.x*100),.38);
                color=lerp(color,lerp(_Blue.rgb,_Pink.rgb,saturate(bars.x*8)),bar*_Accent*.75*step(.15,_Accent));

                // Dark stages retain a dim centre so bullets and character silhouettes dominate.
                float vignette=saturate(dot(p*float2(.55,1),p*float2(.55,1))*.55);
                color*=1-vignette*_Dark*.38;
                return half4(color*input.color.rgb,input.color.a);
            }
            ENDHLSL
        }
    }
}
