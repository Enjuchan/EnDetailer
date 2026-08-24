using System;
using System.Collections.Generic;
using EnDetailer.Core;

namespace EnDetailer;

/// <summary>
/// Die im Spiel gebraeuchlichen Job-Farben. Intern rechnet ImGui mit ABGR,
/// notiert sind sie hier als lesbares RGB.
/// </summary>
public static class JobColors
{
    private static readonly Dictionary<Job, uint> Rgb = new()
    {
        // Tanks
        [Job.PLD] = 0xA8D2E6, [Job.GLA] = 0xA8D2E6,
        [Job.WAR] = 0xCF2621, [Job.MRD] = 0xCF2621,
        [Job.DRK] = 0xD126CC,
        [Job.GNB] = 0x796D30,

        // Heiler
        [Job.WHM] = 0xFFF0DC, [Job.CNJ] = 0xFFF0DC,
        [Job.SCH] = 0x8657FF,
        [Job.AST] = 0xFFE74A,
        [Job.SGE] = 0x80A0F0,

        // Nahkampf
        [Job.MNK] = 0xD69C00, [Job.PGL] = 0xD69C00,
        [Job.DRG] = 0x4164CD, [Job.LNC] = 0x4164CD,
        [Job.NIN] = 0xAF1964, [Job.ROG] = 0xAF1964,
        [Job.SAM] = 0xE46D04,
        [Job.RPR] = 0x965A90,
        [Job.VPR] = 0x108210,

        // Fernkampf physisch
        [Job.BRD] = 0x91BA5E, [Job.ARC] = 0x91BA5E,
        [Job.MCH] = 0x6EE1C2,
        [Job.DNC] = 0xE2B0AF,

        // Magier
        [Job.BLM] = 0xA579D6, [Job.THM] = 0xA579D6,
        [Job.SMN] = 0x2D9B78, [Job.ACN] = 0x2D9B78,
        [Job.RDM] = 0xE87B7B,
        [Job.BLU] = 0x2C9AD8,
        [Job.PCT] = 0xFC92E1
    };

    private const uint UnknownRgb = 0x9C9C9C;

    /// <summary>Volle Farbe, etwa fuer Linien im Verlaufsfenster.</summary>
    public static uint For(Job job) => ToAbgr(Rgb.GetValueOrDefault(job, UnknownRgb), 0xFF);

    /// <summary>Gedaempfte Fassung fuer die Balken hinter den Namen.</summary>
    public static uint Bar(Job job, byte alpha) => ToAbgr(Rgb.GetValueOrDefault(job, UnknownRgb), alpha);

    /// <summary>Aufgehellte Fassung fuer die Leuchtkante am Balkenende.</summary>
    public static uint Glow(Job job)
    {
        var rgb = Rgb.GetValueOrDefault(job, UnknownRgb);
        var r = Math.Min(255, ((rgb >> 16) & 0xFF) + 70);
        var g = Math.Min(255, ((rgb >> 8) & 0xFF) + 70);
        var b = Math.Min(255, (rgb & 0xFF) + 70);
        return ToAbgr((r << 16) | (g << 8) | b, 0xB0);
    }

    private static uint ToAbgr(uint rgb, byte alpha)
    {
        var r = (rgb >> 16) & 0xFF;
        var g = (rgb >> 8) & 0xFF;
        var b = rgb & 0xFF;
        return ((uint)alpha << 24) | (b << 16) | (g << 8) | r;
    }
}
