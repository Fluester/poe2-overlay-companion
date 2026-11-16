using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion.Localization;

public static class Loc
{
    public static AppLanguage Current { get; set; } = AppLanguage.Uk;

    public static string T(string uk, string en, string ru) => Current switch
    {
        AppLanguage.En => en,
        AppLanguage.Ru => ru,
        _ => uk
    };

    public static string AiLanguageInstruction() => Current switch
    {
        AppLanguage.En => "Respond ONLY IN ENGLISH. Keep PoE 2 skill, item, and passive names exact.",
        AppLanguage.Ru => "Отвечай ТОЛЬКО НА РУССКОМ ЯЗЫКЕ, кратко и структурировано. Оригинальные названия камней, предметов и нодов оставляй на английском (или в скобках).",
        _ => "Відповідай ТІЛЬКИ УКРАЇНСЬКОЮ МОВОЮ, коротко і структуровано. Оригінальні назви каменів, предметів і нодів залишай англійською (або в дужках)."
    };

    // Навігація (чисті короткі заголовки без спаму емодзі)
    public static string NavOverview => T("Огляд білду", "Build Overview", "Обзор билда");
    public static string NavPassives => T("Дерево пасивок", "Passive Tree", "Дерево пассивок");
    public static string NavItems => T("Предмети", "Item Check", "Предметы");
    public static string NavPlanner => T("Планувальник", "Build Planner", "Планировщик");
    public static string NavFarm => T("Трекер сесії", "Farm Tracker", "Трекер сессии");
    public static string NavUpdates => T("Оновлення", "Updates", "Обновления");
    public static string NavAi => T("AI-радник", "AI Advisor", "AI-советник");
    public static string NavBuilds => T("Імпорт і Профілі", "Builds & Import", "Импорт и Профили");
    public static string NavSettings => T("Налаштування", "Settings", "Настройки");

    // Шапка
    public static string LevelShort => T("Рів.", "Lvl", "Ур.");
    public static string NoBuildSelected => T("Білди не додано", "No build added", "Билды не добавлены");
    public static string LogNotFound => T("Лог не знайдено", "Log not found", "Лог не найден");
    public static string ClickLog => T("Натисни «Лог»", "Select Log", "Выбери Лог");
    public static string BtnLog => T("Лог", "Log", "Лог");
    public static string BtnAllowTyping => T("Режим вводу", "Enable Input", "Режим ввода");
    public static string BtnLockFocus => T("Готово", "Lock Focus", "Готово");

    // Секції білду
    public static string LblActiveSkills => T("Активні уміння та підтримка", "Active Skills & Supports", "Активные умения и поддержка");
    public static string LblUnlocksNext => T("Відкриється далі по рівню", "Unlocks Next by Level", "Откроется дальше по уровню");
    public static string LblGearTargets => T("Цільове спорядження", "Equipment Targets", "Целевая экипировка");
    public static string LblUpcomingGear => T("Наступні апгрейди екіпу", "Upcoming Equipment", "Будущие апгрейды");
    public static string LblPassivesSummary => T("Пасивне дерево та ключові ноди", "Passive Tree & Key Nodes", "Пассивное дерево и ключевые ноды");
    public static string LblPointsShort => T("очків", "pts", "очков");
    public static string LblAllLevels => T("Усі рівні", "All levels", "Все уровни");
    public static string LblNoBuildsYet => T(
        "Поки не додано жодного білду.\nІмпортуй свій білд пакетно (.build файли / папку), через код PoB2, pobb.in або посилання на гайд — і оверлей автоматично підлаштується під нього.",
        "No builds added yet.\nBatch-import your .build files or folder, paste a PoB2 / pobb.in code, or link a guide URL to tailor the overlay to your character.",
        "Пока не добавлено ни одного билда.\nИмпортируй свой билд пакетно (.build файлы / папку), через код PoB2, pobb.in или ссылку на гайд — и оверлей подстроится под него."
    );

    public static string Verdict(ItemVerdict v) => v switch
    {
        ItemVerdict.Great => T("ВІДМІННО / BiS", "GREAT / BiS", "ОТЛИЧНО / BiS"),
        ItemVerdict.Good => T("ДОБРЕ", "GOOD", "ХОРОШО"),
        ItemVerdict.Ok => T("БАЗОВО / НОРМ", "OK / USABLE", "БАЗОВО / НОРМ"),
        ItemVerdict.Warn => T("СЛАБКО / УВАГА", "WARN / MISMATCH", "СЛАБО / ВНИМАНИЕ"),
        ItemVerdict.Danger => T("КРИТИЧНО!", "CRITICAL!", "КРИТИЧНО!"),
        _ => T("ІНФО", "INFO", "ИНФО")
    };
}
