namespace StationeersIC10Editor;

using System.IO;

using UnityEngine;

// Sprite parts from the asset zip (assets/egg/images, see EggAssets) and sprite composition.
public static class EggSprites
{
    // DJ sprite layout in image coordinates (y down) on a square canvas: the GitHub avatar
    // (assumed 460 px square, sampled by UV so other sizes work too) masked to the silhouette,
    // then the Inaki headphones (assets/egg/headphones.png, 457x411) on top.
    const int DjCanvas = 500;
    const int HeadphonesX = 21;
    const int HeadphonesY = 0;
    const float FaceDrawSize = 460f * 0.88f;
    const int FaceX = 46;
    const int FaceY = 77;

    static Texture2D _headphones;
    static Texture2D _faceMask;

    public static Texture2D Headphones => _headphones ??= LoadImage("headphones.png");
    public static Texture2D FaceMask => _faceMask ??= LoadImage("facemask.png");

    // Needs the asset zip to be installed (EggAssets.Ready).
    public static Texture2D LoadImage(string name)
    {
        var path = EggAssets.ImagePath(name);
        if (!File.Exists(path))
        {
            L.Debug($"Asset image {path} not found");
            return null;
        }
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.LoadImage(File.ReadAllBytes(path));
        return texture;
    }

    // Composing takes ~200 ms (per-pixel bilinear sampling), so the result is cached on disk.
    public static Texture2D CachedDj(Texture2D face)
    {
        var path = Path.Combine(EggAssets.AssetsDir, "textures", $"dj_{EggAssets.Version}.png");
        if (File.Exists(path))
        {
            var cached = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            cached.LoadImage(File.ReadAllBytes(path));
            return cached;
        }
        var dj = ComposeDj(face);
        if (dj != null && dj != face)
            File.WriteAllBytes(path, dj.EncodeToPNG());
        return dj;
    }

    public static Texture2D ComposeDj(Texture2D face)
    {
        var headphones = Headphones;
        var mask = FaceMask;
        if (face == null || headphones == null || mask == null)
            return face;

        var pixels = new Color[DjCanvas * DjCanvas];
        for (var y = 0; y < DjCanvas; y++)
            for (var x = 0; x < DjCanvas; x++)
            {
                var color = Color.clear;

                var fu = (x - FaceX) / FaceDrawSize;
                var fv = (y - FaceY) / FaceDrawSize;
                if (fu >= 0f && fu < 1f && fv >= 0f && fv < 1f)
                {
                    color = face.GetPixelBilinear(fu, 1f - fv);
                    color.a = mask.GetPixelBilinear(fu, 1f - fv).r;
                }

                var hu = (x - HeadphonesX) / (float)headphones.width;
                var hv = (y - HeadphonesY) / (float)headphones.height;
                if (hu >= 0f && hu < 1f && hv >= 0f && hv < 1f)
                    color = AlphaOver(headphones.GetPixelBilinear(hu, 1f - hv), color);

                pixels[(DjCanvas - 1 - y) * DjCanvas + x] = color;
            }

        var result = new Texture2D(DjCanvas, DjCanvas, TextureFormat.RGBA32, false);
        result.SetPixels(pixels);
        result.Apply();
        return result;
    }

    static Color AlphaOver(Color top, Color bottom)
    {
        var alpha = top.a + bottom.a * (1f - top.a);
        if (alpha <= 0f)
            return Color.clear;
        var color = (top * top.a + bottom * (bottom.a * (1f - top.a))) / alpha;
        color.a = alpha;
        return color;
    }
}
