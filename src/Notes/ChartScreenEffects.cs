using System.Reflection;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SinmaiAlpha.ChartVisuals;
using SinmaiAlpha.Notes.Libs;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SinmaiAlpha.Notes;

// A private image-space frame for one native player/reader. Input, judgment,
// clocks and score remain native. Never sample the other player's viewport.
internal sealed class ChartScreenEffects : IDisposable
{
    private static readonly string[] Kinds = { "gaussian", "neon", "trail", "brightness", "saturation", "contrast", "rainbow", "vignette", "glitch", "tvnoise", "hue", "flash", "tint", "shake" };
    private readonly IDictionary<string, PresentationCommands.Track> tracks;
    private Material effect, modernEffect, legacyEffect, copy;
    private RenderTexture input, output;
    private readonly RenderTexture[] history = new RenderTexture[3];
    private bool acquired, failed, trailActive;
    private int trailSeed, trailFrames;
    private double previousTime = double.NegativeInfinity;
    private PresentationChange tintSource;
    private Vector4 tintColor;

    internal ChartScreenEffects(IDictionary<string, PresentationCommands.Track> tracks)
    {
        this.tracks = tracks;
        // Compile and allocate the shared material when the chart is bound,
        // rather than at the first effect, which may be at the song's ending.
        var previous = RenderTexture.active;
        RenderTexture warm = null;
        try
        {
            EnsureMaterials();
            warm = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGB32);
            effect.SetVector("_PlayfieldRect", new Vector4(0, 0, 1, 1));
            foreach (var filter in ExtraScreenFilters.All) if (tracks.ContainsKey(filter.Kind)) effect.SetFloat(filter.Uniform, 1);
            Graphics.Blit(Texture2D.blackTexture, warm, effect);
            foreach (var filter in ExtraScreenFilters.All) effect.SetFloat(filter.Uniform, 0);
        }
        catch (Exception error)
        { failed = true; MelonLogger.Warning("[Chart Effects] Warmup unavailable: " + error.Message); Dispose(); }
        finally { if (warm != null) RenderTexture.ReleaseTemporary(warm); RenderTexture.active = previous; }
    }
    internal static bool HasTracks(IDictionary<string, PresentationCommands.Track> tracks)
    { if (tracks == null) return false; foreach (var kind in Kinds) if (tracks.ContainsKey(kind)) return true; foreach (var filter in ExtraScreenFilters.All) if (tracks.ContainsKey(filter.Kind)) return true; return false; }
    private float Value(string kind, double now) => tracks.TryGetValue(kind, out var track) ? track.Evaluate(now) : 0;
    private bool Upcoming(double now) => tracks.TryGetValue("trail", out var track) && track.IsUpcoming(now, .5f);

    internal bool TryDraw(RenderTexture source, RenderTexture destination, PresentationRect bounds, PresentationRect visible, PresentationRect playfield, double now)
    {
        bounds = playfield;
        visible = visible.Intersect(playfield);
        if (failed || !bounds.HasArea || !visible.HasArea)
        {
            return false;
        }
        RenderTexture active = RenderTexture.active;
        try
        {
            float num = (float)(now * 0.001);
            float num2 = (float)(previousTime * 0.001);
            bool advanced = num != num2;
            if (num < num2 || num - num2 > 0.5f)
            {
                ReleaseHistory();
            }
            previousTime = now;
            bool flag = Active(now);
            bool flag2 = tracks.ContainsKey("trail");
            EnsureMaterials();
            PresentationChange kaleidoscopeSource = null;
            if (tracks.TryGetValue("kaleidoscope", out var kaleidoscopeTrack)) kaleidoscopeTrack.Evaluate(now, out kaleidoscopeSource);
            effect = kaleidoscopeSource != null && !kaleidoscopeSource.AngleMode ? legacyEffect : modernEffect;
            int num15 = Math.Max(1, Mathf.RoundToInt((float)source.width * bounds.Width));
            int num16 = Math.Max(1, Mathf.RoundToInt((float)source.height * bounds.Height));
            EnsureTargets(num15, num16, source.format);
            if (flag2)
            {
                EnsureHistory();
            }
            if (!flag && !flag2)
            {
                trailActive = false;
                trailSeed = 0;
                return false;
            }
            // `input` is read by the effect pass (flag), the seed and the history shift. Seed and shift run on the
            // GPU only while TrailMayShow (their copies cannot be read otherwise); trailSeed/trailFrames advance
            // exactly as before either way, so which frames seed and shift does not change.
            if (double.IsNaN(now))
            {
                trailUngated = true;
            }
            bool flag3 = flag2 && TrailMayShow(now);
            bool flag4 = flag2 && trailSeed < 3;
            bool flag5 = flag2 && !flag4 && advanced && trailFrames >= 4;
            if (flag || (flag3 && (flag4 || flag5)))
            {
                Capture(source, bounds, visible);
            }
            if (flag2)
            {
                EnsureHistory();
                if (trailSeed < 3)
                {
                    if (flag3)
                    {
                        for (int j = 0; j < history.Length; j++)
                        {
                            Graphics.Blit(input, history[j]);
                        }
                    }
                    trailSeed = 3;
                    trailFrames = 0;
                }
                trailActive = true;
            }
            else
            {
                trailActive = false;
                trailSeed = 0;
            }
            if (!flag)
            {
                AdvanceHistory(advanced, flag3);
                return false;
            }
            // Uniform values are needed only for a draw; Active already checked idle frames.
            float num3 = Value("gaussian", now);
            float num4 = Value("neon", now);
            float num5 = Mathf.Clamp01(Value("trail", now));
            float num6 = Value("brightness", now);
            float num7 = Mathf.Clamp01(Value("saturation", now));
            float num8 = Value("contrast", now);
            float num9 = Mathf.Clamp01(Value("rainbow", now));
            float num10 = Mathf.Clamp01(Value("vignette", now));
            float num11 = Mathf.Clamp01(Value("glitch", now));
            float num12 = Mathf.Clamp01(Value("tvnoise", now));
            float num13 = Value("hue", now) * ((float)Math.PI / 180f);
            float num14 = Mathf.Clamp(Value("flash", now), -1f, 1f);
            Vector4 value = Tint(now);
            Vector2 vector = Shake(now);
            effect.SetFloat("_Blur", num3);
            effect.SetFloat("_Neon", num4);
            effect.SetFloat("_Trail", num5);
            effect.SetFloat("_Brightness", num6);
            effect.SetFloat("_Saturation", num7);
            effect.SetFloat("_Contrast", num8);
            effect.SetFloat("_Rainbow", num9);
            effect.SetFloat("_Vignette", num10);
            effect.SetFloat("_Glitch", num11);
            PresentationChange source2 = null;
            if (tracks.TryGetValue("vignette", out var value2))
            {
                value2.Evaluate(now, out source2);
            }
            effect.SetFloat("_VignetteGradient", (source2 != null && source2.Gradient) ? 1 : 0);
            effect.SetFloat("_TVNoise", num12);
            effect.SetFloat("_Hue", num13);
            effect.SetFloat("_Flash", num14);
            effect.SetFloat("_EffectTime", (float)(now * 0.001));
            effect.SetVector("_TintColor", value);
            effect.SetFloat("_OffsetX", vector.x);
            effect.SetFloat("_OffsetY", vector.y);
            effect.SetFloat("_Rotate", 0f);
            effect.SetFloat("_Zoom", 0f);
            effect.SetFloat("_Fade", 0f);
            effect.SetFloat("_AlphaClipSpace", 0f);
            Rect rect = Relative(playfield, bounds);
            effect.SetVector("_PlayfieldRect", new Vector4(rect.x, rect.y, rect.xMax, rect.yMax));
            effect.SetVector("_MainTex_TexelSize", new Vector4(1f / (float)num15, 1f / (float)num16, num15, num16));
            ExtraScreenFilters.Definition[] all = ExtraScreenFilters.All;
            foreach (ExtraScreenFilters.Definition definition2 in all)
            {
                PresentationChange source3 = null;
                float value3 = (tracks.TryGetValue(definition2.Kind, out var value4) ? value4.Evaluate(now, out source3) : 0f);
                effect.SetFloat(definition2.Uniform, value3);
                if (definition2.Kind == "kaleidoscope") effect.SetFloat("_KaleidoscopeAngle", (value4?.EvaluateKaleidoscopeAngle(now) ?? 0) * Mathf.Deg2Rad);
                if (definition2.Kind == "shatter" || definition2.Kind == "ripple")
                {
                    effect.SetFloat(definition2.Uniform + "Phase", (source3 == null) ? 0f : ((float)(now * 0.001) - (float)(source3.Time * 0.001)));
                }
            }
            for (int k = 0; k < 3; k++)
            {
                effect.SetTexture("_HistoryTex" + ((k == 0) ? "" : (k + 1).ToString()), trailActive ? ((Texture)history[k]) : ((Texture)Texture2D.blackTexture));
            }
            Graphics.Blit(input, output, effect);
            AdvanceHistory(advanced, flag3);
            Rect uv = Relative(visible, bounds);
            Draw(destination, PixelRect(visible, source.width, source.height), output, uv);
            return true;
        }
        catch (Exception ex)
        {
            failed = true;
            MelonLogger.Warning("[Chart Effects] Player frame effects unavailable: " + ex.Message);
            Dispose();
            return false;
        }
        finally
        {
            RenderTexture.active = active;
        }
    }


    private Vector2 Shake(double now)
    {
        if (!tracks.TryGetValue("shake", out var track)) return Vector2.zero;
        var amount = track.Evaluate(now, out var source);
        if (source == null || Mathf.Abs(amount) <= .0001f) return Vector2.zero;
        var phase = (float)(now * .001) * (source.Frequency > 0 ? source.Frequency : 18);
        var x = (Mathf.PerlinNoise(phase, .37f) - .5f) * 2 * amount * .05f;
        return source.HasDirection ? new Vector2(Mathf.Cos(source.Direction) * x, Mathf.Sin(source.Direction) * x) :
            new Vector2(x, (Mathf.PerlinNoise(.71f, phase) - .5f) * 2 * amount * .05f);
    }
    private Vector4 Tint(double now)
    {
        if (!tracks.TryGetValue("tint", out var track)) return Vector4.zero;
        var amount = Mathf.Clamp01(track.Evaluate(now, out var source));
        if (amount <= .001f || source == null || string.IsNullOrEmpty(source.ColorHex)) return Vector4.zero;
        if (tintSource != source)
        {
            var rgb = int.Parse(source.ColorHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            tintColor = new Vector4(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 0); tintSource = source;
        }
        var result = tintColor; result.w = amount; return result;
    }
    private void EnsureMaterials()
    {
        if (effect != null && copy != null) return;
        var shader = EffectAssets.Acquire(); acquired = true;
        effect = modernEffect = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        legacyEffect = new Material(EffectAssets.Legacy) { hideFlags = HideFlags.HideAndDontSave };
        copy = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        blit = CreateBlit(shader);
    }
    private void EnsureTargets(int width, int height, RenderTextureFormat format)
    {
        if (input != null && input.width == width && input.height == height && input.format == format) return;
        Release(ref input); Release(ref output); ReleaseHistory();
        input = Create(width, height, format); output = Create(width, height, format);
    }
    private static RenderTexture Create(int width, int height, RenderTextureFormat format)
    {
        var texture = new RenderTexture(width, height, 0, format) { antiAliasing = 1, hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        if (!texture.Create()) { Object.Destroy(texture); throw new InvalidOperationException("RenderTexture creation failed"); }
        return texture;
    }
    private void EnsureHistory()
    {
        if (history[0] != null) return;
        for (var i = 0; i < history.Length; i++) { history[i] = Create(input.width, input.height, input.format); history[i].filterMode = FilterMode.Point; history[i].wrapMode = TextureWrapMode.Repeat; Graphics.Blit(Texture2D.blackTexture, history[i]); }
    }
    private void Capture(RenderTexture source, PresentationRect bounds, PresentationRect visible)
    {
        var uv = Relative(visible, bounds);
        var pixels = new Rect(uv.x * input.width, (1 - uv.y - uv.height) * input.height, uv.width * input.width, uv.height * input.height);
        Graphics.SetRenderTarget(input); GL.Clear(false, true, Color.black);
        Draw(input, pixels, source, new Rect(visible.X, visible.Y, visible.Width, visible.Height));
    }
    private static Rect Relative(PresentationRect visible, PresentationRect bounds) =>
        new((visible.X - bounds.X) / bounds.Width, (visible.Y - bounds.Y) / bounds.Height, visible.Width / bounds.Width, visible.Height / bounds.Height);
    private static Rect PixelRect(PresentationRect rect, int width, int height) => new(rect.X * width, (1 - rect.Top) * height, rect.Width * width, rect.Height * height);
    private void Draw(RenderTexture target, Rect pixels, Texture texture, Rect uv)
    {
        Graphics.SetRenderTarget(target);
        GL.PushMatrix();
        try
        {
            GL.LoadPixelMatrix(0f, (target != null) ? target.width : Screen.width, (target != null) ? target.height : Screen.height, 0f);
            // `blit` only into a render texture (full-target viewport) and where texels land 1:1 on pixels: every
            // fetch is then at a texel centre, where copy's uv round trip ((u - 0.5) * a / a + 0.5, ~2e-4 texel) and
            // this offset stay well inside half a filter-weight step, so both give the texel's bytes. Else `copy`.
            Graphics.DrawTexture(pixels, texture, uv, 0, 0, 0, 0, Color.white, (blit != null && target != null && TexelAligned(pixels, texture, uv)) ? blit : copy);
        }
        finally
        {
            GL.PopMatrix();
        }
    }
    private static void Release(ref RenderTexture texture)
    { if (texture != null) { texture.Release(); Object.Destroy(texture); texture = null; } }
    private void ReleaseHistory()
    { for (var i = 0; i < history.Length; i++) Release(ref history[i]); trailSeed = trailFrames = 0; trailActive = false; }
    public void Dispose()
    {
        Release(ref input); Release(ref output); ReleaseHistory();
        if (modernEffect != null) Object.Destroy(modernEffect); if (legacyEffect != null) Object.Destroy(legacyEffect);
        if (copy != null) Object.Destroy(copy); if (blit != null) Object.Destroy(blit);
        effect = modernEffect = legacyEffect = copy = blit = null;
        if (acquired) { acquired = false; EffectAssets.Release(); }
    }

    private static class EffectAssets
    {
        private static AssetBundle bundle;
        private static Shader shader;
        internal static Shader Legacy;
        private static int owners;
        internal static Shader Acquire()
        {
            if (shader == null)
            {
                using var stream = SinmaiAlpha.Hosting.AssetFiles.Open("Alpha/AlphaScreenEffects.ab");
                if (stream == null) throw new InvalidOperationException("Compiled effect resource missing");
                using var memory = new MemoryStream(); stream.CopyTo(memory);
                bundle = AssetBundle.LoadFromMemory(memory.ToArray());
                shader = bundle != null ? bundle.LoadAsset<Shader>("assets/aquamai/alphascreeneffects.shader") : null;
                Legacy = bundle != null ? bundle.LoadAsset<Shader>("assets/aquamai/alphascreeneffectslegacy.shader") : null;
                if (shader == null || !shader.isSupported || Legacy == null || !Legacy.isSupported) { if (bundle != null) bundle.Unload(true); bundle = null; shader = Legacy = null; throw new InvalidOperationException("Compiled effect shader unsupported"); }
            }
            owners++; return shader;
        }
        internal static void Release()
        { if (--owners > 0) return; if (bundle != null) bundle.Unload(true); bundle = null; shader = Legacy = null; owners = 0; }
    }


    private Material blit;

    private double activeTime = double.NaN;

    private bool activeValue;

    private PresentationCommands.Track trailWindowsTrack;

    private double[] trailWindows;

    private bool trailUngated;

    private void AdvanceHistory(bool advanced, bool gpu)
    {
        if (trailActive && advanced && ++trailFrames >= 5)
        {
            if (gpu)
            {
                // Was Blit(history[1] -> [2]), Blit([0] -> [1]), Blit(input -> [0]): same-size texel copies, so moving
                // the textures leaves every slot with the same bytes. Each takes its slot's sampler state (history
                // Point/Repeat, input Bilinear/Clamp, as created); Capture clears and redraws input before any read.
                RenderTexture renderTexture = history[2];
                history[2] = history[1];
                history[1] = history[0];
                history[0] = input;
                input = renderTexture;
                history[0].filterMode = FilterMode.Point;
                history[0].wrapMode = TextureWrapMode.Repeat;
                input.filterMode = FilterMode.Bilinear;
                input.wrapMode = TextureWrapMode.Clamp;
            }
            trailFrames = 0;
        }
    }

    private bool Active(double now)
    {
        if (now == activeTime)
        {
            return activeValue;
        }
        float num = Value("gaussian", now);
        float num2 = Value("neon", now);
        float num3 = Mathf.Clamp01(Value("trail", now));
        float num4 = Value("brightness", now);
        float num5 = Mathf.Clamp01(Value("saturation", now));
        float num6 = Value("contrast", now);
        float num7 = Mathf.Clamp01(Value("rainbow", now));
        float num8 = Mathf.Clamp01(Value("vignette", now));
        float num9 = Mathf.Clamp01(Value("glitch", now));
        float num10 = Mathf.Clamp01(Value("tvnoise", now));
        float num11 = Value("hue", now) * ((float)Math.PI / 180f);
        float num12 = Mathf.Clamp(Value("flash", now), -1f, 1f);
        Vector4 vector = Tint(now);
        Vector2 vector2 = Shake(now);
        bool flag = Mathf.Abs(num) > 0.001f || Mathf.Abs(num2) > 0.001f || num3 > 0.001f || Mathf.Abs(num4) > 0.001f || num5 > 0.001f || Mathf.Abs(num6) > 0.001f || num7 > 0.001f || num8 > 0.001f || num9 > 0.001f || num10 > 0.001f || Mathf.Abs(num11) > 0.0001f || Mathf.Abs(num12) > 0.001f || vector.w > 0.001f || Mathf.Abs(vector2.x) > 0.0001f || Mathf.Abs(vector2.y) > 0.0001f;
        ExtraScreenFilters.Definition[] all = ExtraScreenFilters.All;
        foreach (ExtraScreenFilters.Definition definition in all)
        {
            flag |= Mathf.Abs(Value(definition.Kind, now)) > 0.0001f;
        }
        activeTime = now;
        activeValue = flag;
        return flag;
    }

    internal bool WillDraw(double now)
    {
        try
        {
            return !failed && Active(now);
        }
        catch (Exception)
        {
            return true;
        }
    }

    private bool TrailMayShow(double now)
    {
        if (trailUngated || !tracks.TryGetValue("trail", out var value))
        {
            return true;
        }
        if (value != trailWindowsTrack)
        {
            trailWindows = TrailWindows(value);
            trailWindowsTrack = value;
        }
        double[] array = trailWindows;
        if (array == null)
        {
            return true;
        }
        double num = now * 0.001;
        for (int i = 0; i < array.Length; i += 2)
        {
            if (array[i] <= num + 8.0 && array[i + 1] >= num)
            {
                return true;
            }
        }
        return false;
    }

    private static double[] TrailWindows(PresentationCommands.Track track)
    {
        try
        {
            object obj = typeof(PresentationCommands.Track).GetField("screenEffect", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(track);
            if (!(obj?.GetType().GetField("allEvents", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(obj) is List<PresentationChange> list))
            {
                return null;
            }
            List<double> list2 = new List<double>();
            double num = double.NaN;
            foreach (PresentationChange item in list)
            {
                double num2 = item.Time * 0.001;
                if (double.IsNaN(num2) || double.IsInfinity(num2))
                {
                    return null;
                }
                double num3 = (item.Duration >= 0f && !float.IsInfinity(item.Duration)) ? (num2 + (double)item.Duration) : double.PositiveInfinity;
                if (item.ShakeMode != 0)
                {
                    if (item.Target != 0f)
                    {
                        list2.Add(num2 - 0.05);
                        list2.Add(num3 + 0.05);
                    }
                    continue;
                }
                if (!double.IsNaN(num))
                {
                    list2.Add(num - 0.05);
                    list2.Add(num2 + 0.05);
                    num = double.NaN;
                }
                if (item.ShakeEnabled && item.Target != 0f)
                {
                    num = num2;
                }
                else
                {
                    list2.Add(num2 - 0.05);
                    list2.Add(num3 + 0.05);
                }
            }
            if (!double.IsNaN(num))
            {
                list2.Add(num - 0.05);
                list2.Add(double.PositiveInfinity);
            }
            return list2.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Material CreateBlit(Shader effectShader)
    {
        Shader shader = Shader.Find("Hidden/BlitCopy");
        if (shader != null && shader.isSupported)
        {
            Material material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            material.SetColor("_Color", Color.white);
            return material;
        }
        Material material2 = new Material(effectShader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        material2.SetVector("_PlayfieldRect", new Vector4(2f, 2f, 3f, 3f));
        return material2;
    }

    private static bool TexelAligned(Rect pixels, Texture texture, Rect uv)
    {
        float num = (float)texture.width;
        float num2 = (float)texture.height;
        float num3 = Mathf.Abs(pixels.x - Mathf.Round(pixels.x)) + Mathf.Abs(uv.x * num - Mathf.Round(uv.x * num)) + Mathf.Abs(uv.width * num - pixels.width);
        float num4 = Mathf.Abs(pixels.y - Mathf.Round(pixels.y)) + Mathf.Abs(uv.y * num2 - Mathf.Round(uv.y * num2)) + Mathf.Abs(uv.height * num2 - pixels.height);
        if (num3 <= 0.001f && num4 <= 0.001f && Mathf.Abs(pixels.width - Mathf.Round(pixels.width)) <= 0.001f)
        {
            return Mathf.Abs(pixels.height - Mathf.Round(pixels.height)) <= 0.001f;
        }
        return false;
    }
}
