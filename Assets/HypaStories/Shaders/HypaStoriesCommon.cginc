// Shared by the Hypa Stories shaders: the prototype's value noise / fbm (Ideation/src/shaders.js) and sRGB -> linear.
#ifndef HYPA_STORIES_COMMON
#define HYPA_STORIES_COMMON

float h21(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
float h31(float3 p) { return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453); }

float vn(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(h21(i), h21(i + float2(1, 0)), f.x), lerp(h21(i + float2(0, 1)), h21(i + float2(1, 1)), f.x), f.y);
}

// 3 octaves instead of the prototype's 4: the fourth is barely visible at headset resolution and costs a quarter of the noise.
float fbm(float2 p)
{
    float a = 0.5, s = 0.0;
    [unroll] for (int i = 0; i < 3; i++) { s += a * vn(p); p *= 2.0; a *= 0.5; }
    return s + 0.0625; // keeps the 4-octave mean so thresholds still match
}

// The prototype wrote sRGB-looking colours and converted with pow(c, 2.2). Same here in Linear colour space.
float3 ToOutput(float3 c)
{
#ifdef UNITY_COLORSPACE_GAMMA
    return c;
#else
    return pow(max(c, 0.0), 2.2);
#endif
}

#endif
