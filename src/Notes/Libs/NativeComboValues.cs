using System;
using Manager;

namespace SinmaiAlpha.Notes.Libs;

// Read-only display data. The caller supplies the native maximum base score
// for the current session (including both sides in a co-op session). No
// preview note counter, score setter, result callback or judge method is used.
public sealed class NativeComboValues
{
    public uint Combo, DeluxeScore;
    public long Score, NormalizedScore, NormalizedDownScore;
    public decimal AchievementClassic, AchievementDownClassic, AchievementDeluxe, AchievementDownDeluxe;

    public static NativeComboValues Capture(GameScoreList score, uint maximumBaseScore)
        => CaptureWithPartner(score, maximumBaseScore, null);

    public static NativeComboValues CaptureSession(GameScoreList score, GameScoreList partner)
    {
        return CaptureSession(score, partner, includeLoss: true);
    }


    private static long Normalize(uint maximum, decimal achievement, float fullRate) =>
        (long)Math.Round((float)maximum * ((float)achievement / fullRate) / 5f) * 5;

    public string Format(int mode)
    {
        switch (mode)
        {
            case 2: return string.Format("{0:#,##0}", Score);
            case 3: return string.Format("{0,6:0.00}%", AchievementClassic);
            case 4: return string.Format("{0,6:0.00}%", AchievementDownClassic);
            case 11: return string.Format("{0,8:0.0000}%", AchievementDeluxe);
            case 12: return string.Format("{0,8:0.0000}%", AchievementDownDeluxe);
            case 13: return DeluxeScore.ToString();
            case 101: return string.Format("{0:#,##0}", NormalizedScore);
            case 102: return string.Format("{0:#,##0}", NormalizedDownScore);
            default: return Combo > 0 ? Combo.ToString() : "";
        }
    }
    public decimal Rate(int mode)
    {
        switch (mode)
        {
            case 3: return AchievementClassic;
            case 4: return AchievementDownClassic;
            case 11: return AchievementDeluxe;
            case 12: return AchievementDownDeluxe;
            default: return 0;
        }
    }


    public static NativeComboValues CaptureSession(GameScoreList score, GameScoreList partner, bool includeLoss)
    {
        if (score == null || !score.IsEnable)
        {
            return new NativeComboValues();
        }
        uint num = score.ScoreTotal._allPerfectScore;
        if (!score.SessionInfo.isUtageCoop || partner == null || !partner.IsEnable)
        {
            partner = null;
        }
        else
        {
            num += partner.ScoreTotal._allPerfectScore;
        }
        return CaptureWithPartner(score, num, partner, score.SessionInfo.isUtageCoop ? 200 : 100, includeLoss);
    }

    private static NativeComboValues CaptureWithPartner(GameScoreList score, uint maximumBaseScore, GameScoreList partner, float fullRate = 100f, bool includeLoss = true)
    {
        NativeComboValues nativeComboValues = new NativeComboValues();
        if (score == null || !score.IsEnable)
        {
            return nativeComboValues;
        }
        nativeComboValues.Combo = score.Combo;
        nativeComboValues.DeluxeScore = score.GetDeluxeScoreAll();
        nativeComboValues.Score = score.TotalScore;
        nativeComboValues.AchievementDeluxe = score.GetAchivement();
        nativeComboValues.AchievementDownDeluxe = score.GetDecAchivement();
        long num = 0L;
        long num2 = 0L;
        if (includeLoss)
        {
            ReadLoss(score, ref num, ref num2);
            if (partner != null)
            {
                ReadLoss(partner, ref num, ref num2);
            }
        }
        if (maximumBaseScore != 0)
        {
            nativeComboValues.AchievementClassic = (decimal)nativeComboValues.Score * 100m / (decimal)maximumBaseScore;
            if (includeLoss)
            {
                // The decimal running sums this replaces only ever added scale-0 integers, far inside both decimal's
                // and long's range, so converting the exact long sums gives the same decimal operands.
                decimal loss = num;
                decimal bonus = num2;
                nativeComboValues.AchievementDownClassic = ((decimal)maximumBaseScore - loss + bonus) * 100m / (decimal)maximumBaseScore;
            }
            nativeComboValues.NormalizedScore = Normalize(maximumBaseScore, nativeComboValues.AchievementDeluxe, fullRate);
            nativeComboValues.NormalizedDownScore = Normalize(maximumBaseScore, nativeComboValues.AchievementDownDeluxe, fullRate);
        }
        return nativeComboValues;
    }

    private static void ReadLoss(GameScoreList score, ref long loss, ref long bonus)
    {
        int scoreLength = score.GetScoreLength();
        for (int i = 0; i < scoreLength; i++)
        {
            JudgeResultSt scoreAt = score.GetScoreAt(i);
            if (scoreAt.Judged && scoreAt.Type != NoteScore.EScoreType.End)
            {
                loss += (long)scoreAt.TheoryScore - (long)scoreAt.Score;
                bonus += scoreAt.ScoreBonus;
            }
        }
    }
}
