Shader "Custom/ExternalOESBlit"
{
    Properties
    {
        _MainTex ("External Video", 2D) = "white" {}
    }
    SubShader
    {
        // Blit sang RenderTexture RGBA
        Tags { "RenderType"="Opaque" }
        ZWrite Off
        Cull Off
        ZTest Always

        Pass
        {
            GLSLPROGRAM
            #ifdef VERTEX
            #if __VERSION__ >= 300
            in vec4 _glesVertex;
            in vec2 _glesMultiTexCoord0;
            out vec2 v_uv;
            uniform mat4 glstate_matrix_mvp;
            void main()
            {
                v_uv = _glesMultiTexCoord0;
                gl_Position = glstate_matrix_mvp * _glesVertex;
            }
            #else
            attribute vec4 _glesVertex;
            attribute vec2 _glesMultiTexCoord0;
            varying vec2 v_uv;
            uniform mat4 glstate_matrix_mvp;
            void main()
            {
                v_uv = _glesMultiTexCoord0;
                gl_Position = glstate_matrix_mvp * _glesVertex;
            }
            #endif
            #endif

            #ifdef FRAGMENT
            precision mediump float;
            #if __VERSION__ >= 300
            #extension GL_OES_EGL_image_external_essl3 : require
            in vec2 v_uv;
            out vec4 fragColor;
            uniform samplerExternalOES _MainTex;
            void main()
            {
                fragColor = texture(_MainTex, v_uv);
            }
            #else
            #extension GL_OES_EGL_image_external : require
            varying vec2 v_uv;
            uniform samplerExternalOES _MainTex;
            void main()
            {
                gl_FragColor = texture2D(_MainTex, v_uv);
            }
            #endif
            #endif
            ENDGLSL
        }
    }
    Fallback Off
}