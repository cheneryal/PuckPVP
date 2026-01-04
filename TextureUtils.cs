using System.IO;
using UnityEngine;

public static class CTP_AssetLoader
{
    public static Texture2D LoadTexture(string fileName, Color fallbackColor)
    {
        // Look in the mod directory
        string path = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), fileName);
        
        if (File.Exists(path))
        {
            byte[] fileData = File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(2, 2);
            tex.LoadImage(fileData); // Auto-resizes
            return tex;
        }

        // Fallback: Create a simple colored 1x1 texture
        Texture2D fallback = new Texture2D(1, 1);
        fallback.SetPixel(0, 0, fallbackColor);
        fallback.Apply();
        return fallback;
    }

    public static Sprite TextureToSprite(Texture2D tex)
    {
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
    }
}