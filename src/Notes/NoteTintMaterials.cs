using Complex = System.Numerics.Complex;
using DB;
using MAI2.Util;
using SinmaiAlpha.Notes.Libs;
using System;
using System.Collections.Generic;
using HarmonyLib;
using Manager;
using Monitor;
using SinmaiAlpha.ChartVisuals;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SinmaiAlpha.Notes;

public partial class CustomNoteTypes
{
    private static Material NoteTintMaterial, NoteTintGaugeMaterial;
    private static readonly Dictionary<SpriteRenderer, Material> BorrowedTintDefaults = new();
    private static readonly Dictionary<SpriteRenderer, MaterialPropertyBlock> NoteTintBlocks = new();
    private static bool HasNoteTintMaterials() => NoteTintMaterial != null || NoteTintGaugeMaterial != null;

    private static readonly HashSet<(Monitor.Game.GameCtrl, NotesReader)> TintWarmedReaders = new();
    [HarmonyLib.HarmonyPatch(typeof(Monitor.Game.GameCtrl), "IsReady")]
    public static class NoteTintWarmupPatch
    {
        public static void Postfix(Monitor.Game.GameCtrl __instance, bool __result)
        {
            if (!__result || !FeaturesEnabled(__instance)) return;
            var reader = Manager.NotesManager.Instance(__instance.MonitorIndex)?.getReader();
            if (reader == null || TintWarmedReaders.Contains((__instance, reader))) return;
            // CPU each-guide baking is spread across loading frames by EachGuideTintPrewarmPatch.
            var sprite = Process.GameNoteImageContainer.NormalTap[0];
            if (sprite == null) return;
            var obj = new GameObject("Sinmai-Alpha tint warmup");
            var render = obj.AddComponent<SpriteRenderer>(); render.enabled = false;
            try
            {
                if (ApplyNoteTintMaterial(render, sprite, Color.white, 0xFFFFFF, 1))
                    render.sharedMaterial.SetPass(0);
            }
            finally { NoteTintBlocks.Remove(render); Object.Destroy(obj); }
            TintWarmedReaders.Add((__instance, reader));
        }
    }
    private static bool ApplyNoteTintMaterial(SpriteRenderer render, Sprite source, Color vertex, int? rgb, float? alpha, Material native = null, bool grayscale = false)
    {
        if (render == null || source == null || !LoadTouchProgressShader())
        {
            return false;
        }
        bool flag = native != null && native.shader != null && native.shader.name == "Custom/AroundFillShade";
        if (flag && !MineTextures.ContainsKey("touchhold_progress_mask"))
        {
            return false;
        }
        Material sharedMaterial;
        if (flag)
        {
            if (NoteTintGaugeMaterial == null)
            {
                NoteTintGaugeMaterial = new Material(TouchProgressShader)
                {
                    name = "AquaMai Note Tint Gauge"
                };
            }
            sharedMaterial = NoteTintGaugeMaterial;
        }
        else
        {
            if (NoteTintMaterial == null)
            {
                // A missing or unsupported shader was looked up again on every call (every coloured part, every
                // frame); the answer cannot change for this bundle, so it is latched until the bundle is reloaded.
                if (NoteTintShaderUnavailable)
                {
                    return false;
                }
                Shader shader = TouchProgressBundle.LoadAsset<Shader>("assets/aquamai/referencenotetint.shader");
                if (shader == null || !shader.isSupported)
                {
                    NoteTintShaderUnavailable = true;
                    return false;
                }
                NoteTintMaterial = new Material(shader)
                {
                    name = "AquaMai Note Tint"
                };
            }
            sharedMaterial = NoteTintMaterial;
        }
        render.sprite = source;
        render.sharedMaterial = sharedMaterial;
        render.color = vertex;
        if (!NoteTintBlocks.TryGetValue(render, out var value))
        {
            value = (NoteTintBlocks[render] = new MaterialPropertyBlock());
        }
        render.GetPropertyBlock(value);
        value.SetColor("_Color", Color.white);
        value.SetColor("_NoteColor", rgb.HasValue ? new Color((float)((rgb.Value >> 16) & 0xFF) / 255f, (float)((rgb.Value >> 8) & 0xFF) / 255f, (float)(rgb.Value & 0xFF) / 255f, 1f) : new Color(1f, 1f, 1f, 0f));
        value.SetFloat("_NoteAlpha", Mathf.Clamp01(alpha ?? 1f));
        value.SetFloat("_Brightness", 1f);
        value.SetFloat("_SrcHue", -1f);
        value.SetFloat("_TintCoverage", 0f);
        value.SetFloat("_DarkDetail", 0.35f);
        value.SetFloat("_Grayscale", grayscale ? 1 : 0);
        if (flag)
        {
            value.SetTexture("_ProgressMask", MineTextures["touchhold_progress_mask"]);
            value.SetFloat("_Amount", native.GetFloat("_Amount"));
            value.SetFloat("_MaskScale", source.pixelsPerUnit / 502f);
        }
        render.SetPropertyBlock(value);
        return true;
    }
    private static bool ApplyBorrowedTintMaterial(SpriteRenderer render, Sprite picture, VisualValue value, bool mine)
    {
        if (!BorrowedTintDefaults.TryGetValue(render, out var native)) native = render.sharedMaterial;
        if (ApplyNoteTintMaterial(render, picture, Color.white, value.Color, value.Alpha,
            grayscale: mine && !value.Color.HasValue))
        {
            BorrowedTintDefaults[render] = native;
            return true;
        }
        if (BorrowedTintDefaults.Remove(render))
        { render.sharedMaterial = native; render.SetPropertyBlock(null); }
        return false;
    }
    private static void ReleaseNoteTintMaterials()
    {
        ResetNoteTintBindings();
        if (NoteTintMaterial != null)
        {
            UnityEngine.Object.Destroy(NoteTintMaterial);
        }
        if (NoteTintGaugeMaterial != null)
        {
            UnityEngine.Object.Destroy(NoteTintGaugeMaterial);
        }
        NoteTintMaterial = null;
        NoteTintGaugeMaterial = null;
        if (TouchProgressOwners.Count == 0)
        {
            ResetTouchProgress();
        }
    }

    private static readonly List<UnityEngine.Vector4>[] FanLanePath = new List<UnityEngine.Vector4>[1];

    private static GameObject SlideBaseStarNoteOf(SlideRoot slide)
    {
        AccessTools.FieldRef<SlideRoot, GameObject> baseStarNote = SlideRefs.BaseStarNote;
        return (baseStarNote != null) ? baseStarNote(slide) : Traverse.Create(slide).Field("_baseStarNote").GetValue<GameObject>();
    }

    private static void ApplySlideSvMotionCore(SlideRoot owner, bool fan, NoteData note, CustomSlideNoteData touch, VisualPayload value)
    {
        float value2 = ((SlideRefs.StarLaunchMsec != null) ? SlideRefs.StarLaunchMsec(owner) : Traverse.Create(owner).Field("StarLaunchMsec").GetValue<float>());
        float value3 = ((SlideRefs.StarArriveMsec != null) ? SlideRefs.StarArriveMsec(owner) : Traverse.Create(owner).Field("StarArriveMsec").GetValue<float>());
        float currentMsec = NotesManager.GetCurrentMsec();
        if (value3 <= value2)
        {
            return;
        }
        OptionMirrorID mirror;
        if (!fan && value?.SlideGeometry != null)
        {
            GameObject value4 = SlideBaseStarNoteOf(owner);
            if (!(value4 == null))
            {
                UnityEngine.Vector3 vector = value.SlideGeometry.DisplayPose(value2, value3 - value2, currentMsec, (float localStart, float localDuration, float time) => MajdataSlideProgress(note, localStart, localDuration, time));
                Complex value5 = new Complex(vector.x * 100f, vector.y * 100f);
                Complex value6 = Complex.FromPolarCoordinates(1.0, (double)vector.z * Math.PI / 180.0);
                mirror = Singleton<GamePlayManager>.Instance.GetGameScore(owner.MonitorId).UserOption.MirrorMode;
                Complex complex = Complex.FromPolarCoordinates(1.0, Math.PI / 4.0 * (double)owner.ButtonId);
                value5 = Mirror(value5) * complex;
                value6 = Mirror(value6) * complex;
                value4.transform.localPosition = new UnityEngine.Vector3((float)value5.Real, (float)value5.Imaginary, value4.transform.localPosition.z);
                value4.transform.localRotation = UnityEngine.Quaternion.Euler(0f, 0f, (float)(value6.Phase * 180.0 / Math.PI));
            }
            return;
        }
        float progress = ((currentMsec <= value2) ? 0f : MajdataSlideProgress(note, value2, value3 - value2, currentMsec));
        if (touch?.TouchGeometry != null)
        {
            GameObject value7 = SlideBaseStarNoteOf(owner);
            if (!(value7 == null))
            {
                Complex pointAt = touch.TouchGeometry.GetPointAt(progress);
                Complex tangentAt = touch.TouchGeometry.GetTangentAt(progress);
                Complex complex2 = Complex.FromPolarCoordinates(1.0, Math.PI / 4.0 * (double)owner.ButtonId);
                pointAt = Mirror2(pointAt) * complex2;
                tangentAt = Mirror2(tangentAt) * complex2;
                value7.transform.localPosition = new UnityEngine.Vector3((float)pointAt.Real, (float)pointAt.Imaginary, value7.transform.localPosition.z);
                value7.transform.localRotation = UnityEngine.Quaternion.Euler(0f, 0f, (float)(tangentAt.Phase * 180.0 / Math.PI) + 178.33763f + 18f);
            }
        }
        else
        {
            if (currentMsec < value2)
            {
                return;
            }
            if (fan)
            {
                GameObject[] value8 = ((owner is SlideFan slideFan && SlideRefs.FanBaseStarObjs != null) ? SlideRefs.FanBaseStarObjs(slideFan) : Traverse.Create(owner).Field("_baseStarObjs").GetValue<GameObject[]>());
                List<UnityEngine.Vector4>[] value9 = ((owner is SlideFan slideFan2 && SlideRefs.FanSlideVecList != null) ? SlideRefs.FanSlideVecList(slideFan2) : Traverse.Create(owner).Field("_slideVecList").GetValue<List<UnityEngine.Vector4>[]>());
                List<UnityEngine.Vector4>[] fanLanePath = FanLanePath;
                try
                {
                    for (int num = 0; num < value8.Length; num++)
                    {
                        fanLanePath[0] = value9[num];
                        Pose(value8[num], fanLanePath);
                    }
                }
                finally
                {
                    fanLanePath[0] = null;
                }
            }
            else
            {
                Pose(SlideBaseStarNoteOf(owner), (SlideRefs.SlideVecListList != null) ? SlideRefs.SlideVecListList(owner) : Traverse.Create(owner).Field("_slideVecListList").GetValue<List<List<UnityEngine.Vector4>>>());
            }
        }
        Complex Mirror(Complex complex3)
        {
            return mirror switch
            {
                OptionMirrorID.LR => -Complex.Conjugate(complex3),
                OptionMirrorID.UD => Complex.Conjugate(complex3),
                OptionMirrorID.UDLR => -complex3,
                _ => complex3,
            };
        }
        Complex Mirror2(Complex v)
        {
            return touch.TouchMirror switch
            {
                OptionMirrorID.LR => -Complex.Conjugate(v),
                OptionMirrorID.UD => Complex.Conjugate(v),
                OptionMirrorID.UDLR => -v,
                _ => v,
            };
        }
        void Pose(GameObject star, IList<List<UnityEngine.Vector4>> paths)
        {
            if (!(star == null) && paths.Count != 0)
            {
                float num2 = 0f;
                foreach (List<UnityEngine.Vector4> path in paths)
                {
                    if (path.Count != 0)
                    {
                        num2 += path[path.Count - 1].z;
                    }
                }
                float num3 = num2 * progress;
                UnityEngine.Vector4 a = default;
                UnityEngine.Vector4 b = default;
                float t = 0f;
                foreach (List<UnityEngine.Vector4> path2 in paths)
                {
                    if (path2.Count != 0)
                    {
                        a = (b = path2[path2.Count - 1]);
                        if (num3 <= a.z)
                        {
                            for (int i = 0; i + 1 < path2.Count; i++)
                            {
                                if (path2[i + 1].z >= num3)
                                {
                                    a = path2[i];
                                    b = path2[i + 1];
                                    t = ((b.z > a.z) ? Mathf.Clamp01((num3 - a.z) / (b.z - a.z)) : 0f);
                                    break;
                                }
                            }
                            break;
                        }
                        num3 -= a.z;
                    }
                }
                UnityEngine.Vector4 vector2 = UnityEngine.Vector4.LerpUnclamped(a, b, t);
                UnityEngine.Vector3 localPosition = star.transform.localPosition;
                star.transform.localPosition = new UnityEngine.Vector3(vector2.x, vector2.y, localPosition.z);
                star.transform.localRotation = UnityEngine.Quaternion.Euler(0f, 0f, Mathf.LerpAngle(a.w, b.w, t) + 90f);
            }
        }
    }
}
