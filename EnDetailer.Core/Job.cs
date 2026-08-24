using System;

namespace EnDetailer.Core;

/// <summary>
/// Die Job-Ids des Spiels. IINACT liefert im Feld "Job" nicht die Zahl, sondern
/// das Kuerzel ("GNB", "WHM") - wer eine Zahl erwartet, bekommt nie einen Treffer
/// und faerbt alles grau.
/// </summary>
public enum Job : uint
{
    Unknown = 0,

    GLA = 1, MRD = 3, PLD = 19, WAR = 21, DRK = 32, GNB = 37,
    CNJ = 6, WHM = 24, SCH = 28, AST = 33, SGE = 40,
    PGL = 2, LNC = 4, ROG = 29, MNK = 20, DRG = 22, NIN = 30, SAM = 34, RPR = 39, VPR = 41,
    ARC = 5, BRD = 23, MCH = 31, DNC = 38,
    THM = 7, ACN = 26, BLM = 25, SMN = 27, RDM = 35, BLU = 36, PCT = 42,
    CRP = 8, BSM = 9, ARM = 10, GSM = 11, LTW = 12, WVR = 13, ALC = 14, CUL = 15,
    MIN = 16, BOT = 17, FSH = 18
}

public enum JobRole
{
    Unknown,
    Tank,
    Healer,
    Melee,
    RangedPhysical,
    Caster
}

public static class Jobs
{
    /// <summary>Liest das Kuerzel, das IINACT im Feld "Job" schickt.</summary>
    public static Job Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Job.Unknown;

        var text = raw.Trim();

        // Manche Quellen liefern doch eine Zahl - dann diese verwenden.
        if (uint.TryParse(text, out var numeric))
            return Enum.IsDefined(typeof(Job), numeric) ? (Job)numeric : Job.Unknown;

        return Enum.TryParse<Job>(text, ignoreCase: true, out var job) ? job : Job.Unknown;
    }

    public static JobRole RoleOf(Job job) => job switch
    {
        Job.GLA or Job.MRD or Job.PLD or Job.WAR or Job.DRK or Job.GNB => JobRole.Tank,
        Job.CNJ or Job.WHM or Job.SCH or Job.AST or Job.SGE => JobRole.Healer,
        Job.PGL or Job.LNC or Job.ROG or Job.MNK or Job.DRG or Job.NIN or Job.SAM or Job.RPR or Job.VPR => JobRole.Melee,
        Job.ARC or Job.BRD or Job.MCH or Job.DNC => JobRole.RangedPhysical,
        Job.THM or Job.ACN or Job.BLM or Job.SMN or Job.RDM or Job.BLU or Job.PCT => JobRole.Caster,
        _ => JobRole.Unknown
    };

    /// <summary>Icon-Id des Job-Symbols im Spiel.</summary>
    public static uint IconId(Job job) => job == Job.Unknown ? 0 : 62000u + (uint)job;
}
