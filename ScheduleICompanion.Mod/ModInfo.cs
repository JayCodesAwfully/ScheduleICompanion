using MelonLoader;

[assembly: MelonInfo(typeof(ScheduleICompanion.Mod.CompanionMod), "Schedule I Companion Bridge", "1.6.0", "James")]

// Intentionally no MelonGame attribute here. Schedule I's reported product/developer
// metadata varies between branches, and an incorrect filter allows the assembly to be
// discovered while preventing CompanionMod.OnInitializeMelon from running.
