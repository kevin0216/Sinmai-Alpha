using UnityEngine;

namespace SinmaiAlpha.Difficulty;

public static partial class ExtraDifficulty
{
    // Runtime PNG loading skips Unity's "Alpha Is Transparency" import step.
    // White RGB in invisible pixels otherwise bleeds into bilinear-filtered
    // edges, leaving a light seam where the card and its tab overlap.
    private static void PrepareTextureEdges(Texture2D texture)
    {
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        Color32[] pixels = texture.GetPixels32();
        byte[] distance = new byte[pixels.Length];
        int[] queue = new int[pixels.Length];
        int num = 0;
        int write = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (pixels[i].a != 0)
            {
                distance[i] = 1;
                queue[write++] = i;
            }
        }
        bool changed = false;
        int width = texture.width;
        while (num < write)
        {
            int num2 = queue[num++];
            if (distance[num2] < 3)
            {
                int num3 = num2 % width;
                if (num3 > 0)
                {
                    Extend(num2, num2 - 1);
                }
                if (num3 + 1 < width)
                {
                    Extend(num2, num2 + 1);
                }
                if (num2 >= width)
                {
                    Extend(num2, num2 - width);
                }
                if (num2 + width < pixels.Length)
                {
                    Extend(num2, num2 + width);
                }
            }
        }
        if (changed)
        {
            texture.SetPixels32(pixels);
        }
        // Upload once and drop the CPU copy: after this edge pass the theme textures are only drawn, measured and
        // sliced (FullRect sprites), never read back. Uploads the same pixels Apply(false, false) / LoadImage did.
        texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        void Extend(int source, int target)
        {
            if (distance[target] == 0)
            {
                Color32 color = pixels[source];
                pixels[target] = new Color32(color.r, color.g, color.b, 0);
                distance[target] = (byte)(distance[source] + 1);
                queue[write++] = target;
                changed = true;
            }
        }
    }
}
