// Dan Shervheim
// danielshervheim.com
// August 2019

using UnityEngine;
using UnityEngine.Rendering;

public static class TextureUtilities
{
    public static Texture2D ToTexture2D(this RenderTexture rt)
    {
        if (rt.dimension != TextureDimension.Tex2D)
        {
            throw new System.InvalidCastException("Expected a 2D RenderTexture!");
        }

        TextureFormat format = (rt.format == RenderTextureFormat.ARGBFloat) ? TextureFormat.RGBAFloat : TextureFormat.RGBAHalf;

        Texture2D tmp = new Texture2D(rt.width, rt.height, format, rt.useMipMap);
        tmp.filterMode = rt.filterMode;
        tmp.anisoLevel = rt.anisoLevel;
        tmp.wrapMode = rt.wrapMode;

        //
        ComputeShader helper = Resources.Load("Texture2DToBuffer") as ComputeShader;
        int handle = helper.FindKernel("CSMain");

        helper.SetTexture(handle, "src", rt);
        helper.SetInt("srcWidth", rt.width);
        helper.SetInt("srcHeight", rt.height);

        Color[] pixels = tmp.GetPixels(0);
        ComputeBuffer b = new ComputeBuffer(pixels.Length, 16);  // sizeof(float4) = 4*4
        helper.SetBuffer(handle, "dst", b);

        helper.Dispatch(handle, (rt.width / 8) + 1, (rt.height / 8) + 1, 1);

        b.GetData(pixels);
        b.Release();

        tmp.SetPixels(pixels);
        tmp.Apply();

        return tmp;
    }

    public static Texture3D ToTexture3D(this RenderTexture rt)
    {
        if (rt.dimension != TextureDimension.Tex3D)
        {
            throw new System.InvalidCastException("Expected a 3D RenderTexture!");
        }

        // todo: read from rt into buffer via compute shader, then transfer buffer back to cpu and write pixels into t3d object

        TextureFormat format = (rt.format == RenderTextureFormat.ARGBFloat) ? TextureFormat.RGBAFloat : TextureFormat.RGBAHalf;

        Texture3D tmp = new Texture3D(rt.width, rt.height, rt.volumeDepth, format, rt.useMipMap);
        tmp.filterMode = rt.filterMode;
        tmp.anisoLevel = rt.anisoLevel;
        tmp.wrapMode = rt.wrapMode;
             
        //
        ComputeShader helper = Resources.Load("Texture3DToBuffer") as ComputeShader;
        int handle = helper.FindKernel("CSMain");

        helper.SetTexture(handle, "src", rt);
        helper.SetInt("srcWidth", rt.width);
        helper.SetInt("srcHeight", rt.height);
        helper.SetInt("srcDepth", rt.volumeDepth);

        Color[] pixels = tmp.GetPixels(0);
        ComputeBuffer b = new ComputeBuffer(pixels.Length, 16);  // sizeof(float4) = 4*4
        helper.SetBuffer(handle, "dst", b);

        helper.Dispatch(handle, (rt.width/8) + 1, (rt.height/8) + 1, (rt.volumeDepth/8) + 1);

        b.GetData(pixels);
        b.Release();

        tmp.SetPixels(pixels);
        tmp.Apply();

        return tmp;
    }
}  // TextureUtilities