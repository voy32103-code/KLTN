using Microsoft.EntityFrameworkCore;
using ReqSimulator.API.Models;

namespace ReqSimulator.API.Data;

public static class SeedData
{
    private const string ScenarioTitle = "University Course Registration System";
    private const string ScenarioKey = "university_course_registration";

    public static async Task SeedScenarioV1Async(this IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var scenarioId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var scenario = await db.Scenarios
            .FirstOrDefaultAsync(s => s.Id == scenarioId);

        if (scenario is null)
        {
            scenario = new Scenario
            {
                Id = scenarioId,
                CreatedAt = DateTime.UtcNow
            };
            db.Scenarios.Add(scenario);
        }

        scenario.ScenarioKey = ScenarioKey;
        scenario.Title = ScenarioTitle;
        scenario.Description = "HUFLIT wants to modernize its course registration process through a new online registration system.";
        scenario.Domain = "Education";
        scenario.Difficulty = PersonaDifficulty.Medium;
        scenario.Version = 1;
        scenario.IsActive = true;
        scenario.PublishedAt = scenario.CreatedAt;

        await SeedPersona(db, scenario.Id);
        await SeedHiddenRequirements(db, scenario.Id);
        await SeedScenarioAsync(db, HospitalScenario, HospitalPersona, HospitalRequirements);
        await SeedScenarioAsync(db, InventoryScenario, InventoryPersona, InventoryRequirements);
        await db.SaveChangesAsync();

        logger.LogInformation("Seeded baseline scenarios: university registration, hospital appointment, inventory management.");
    }

    private static async Task SeedPersona(AppDbContext db, Guid scenarioId)
    {
        var persona = await db.Personas
            .FirstOrDefaultAsync(p => p.ScenarioId == scenarioId && p.Name == "Ms. Nguyen");

        if (persona is null)
        {
            persona = new Persona
            {
                Id = Guid.Parse("22222222-2222-4222-8222-222222222222"),
                ScenarioId = scenarioId,
                CreatedAt = DateTime.UtcNow
            };
            db.Personas.Add(persona);
        }

        persona.Name = "Ms. Nguyen";
        persona.RoleTitle = "University Registrar";
        persona.PersonalityTraits = """
            {"traits":["organized","impatient","detail_oriented"],"jargon_level":"medium","cooperation_level":"medium","disclosure_style":"progressive","taboo_topics":["implementation details","database design"]}
            """;
        persona.CommunicationStyle = "formal-busy";
        persona.KnowledgeLevel = "high";
        persona.Difficulty = PersonaDifficulty.Medium;
        persona.InitialMood = "neutral_busy";
        persona.InitialPatience = 0.65m;
    }

    private static async Task SeedHiddenRequirements(AppDbContext db, Guid scenarioId)
    {
        foreach (var seed in HiddenRequirementSeeds)
        {
            var requirement = await db.HiddenRequirements
                .FirstOrDefaultAsync(r => r.Id == seed.Id);

            if (requirement is null)
            {
                requirement = new HiddenRequirement
                {
                    Id = seed.Id,
                    ScenarioId = scenarioId,
                    CreatedAt = DateTime.UtcNow
                };
                db.HiddenRequirements.Add(requirement);
            }

            requirement.RequirementText = seed.RequirementText;
            requirement.Category = seed.Category;
            requirement.RevealDifficulty = seed.RevealDifficulty;
            requirement.RevealCondition = seed.RevealCondition;
            requirement.GateOrder = seed.GateOrder;
        }
    }

    private sealed record HiddenRequirementSeed(
        Guid Id,
        string RequirementText,
        RequirementCategory Category,
        PersonaDifficulty RevealDifficulty,
        string RevealCondition,
        int GateOrder);

    private sealed record ScenarioSeed(
        Guid Id,
        string ScenarioKey,
        string Title,
        string Description,
        string Domain,
        PersonaDifficulty Difficulty,
        int Version);

    private sealed record PersonaSeed(
        Guid Id,
        string Name,
        string RoleTitle,
        string PersonalityTraits,
        string CommunicationStyle,
        string KnowledgeLevel,
        PersonaDifficulty Difficulty,
        string InitialMood,
        decimal InitialPatience);

    private static async Task SeedScenarioAsync(
        AppDbContext db,
        ScenarioSeed scenarioSeed,
        PersonaSeed personaSeed,
        IReadOnlyList<HiddenRequirementSeed> requirements)
    {
        var scenario = await db.Scenarios
            .FirstOrDefaultAsync(s => s.Id == scenarioSeed.Id);

        if (scenario is null)
        {
            scenario = new Scenario
            {
                Id = scenarioSeed.Id,
                CreatedAt = DateTime.UtcNow
            };
            db.Scenarios.Add(scenario);
        }

        scenario.ScenarioKey = scenarioSeed.ScenarioKey;
        scenario.Title = scenarioSeed.Title;
        scenario.Description = scenarioSeed.Description;
        scenario.Domain = scenarioSeed.Domain;
        scenario.Difficulty = scenarioSeed.Difficulty;
        scenario.Version = scenarioSeed.Version;
        scenario.IsActive = true;
        scenario.PublishedAt = scenario.CreatedAt;

        var persona = await db.Personas
            .FirstOrDefaultAsync(p => p.ScenarioId == scenario.Id && p.Name == personaSeed.Name);

        if (persona is null)
        {
            persona = new Persona
            {
                Id = personaSeed.Id,
                ScenarioId = scenario.Id,
                CreatedAt = DateTime.UtcNow
            };
            db.Personas.Add(persona);
        }

        persona.Name = personaSeed.Name;
        persona.RoleTitle = personaSeed.RoleTitle;
        persona.PersonalityTraits = personaSeed.PersonalityTraits;
        persona.CommunicationStyle = personaSeed.CommunicationStyle;
        persona.KnowledgeLevel = personaSeed.KnowledgeLevel;
        persona.Difficulty = personaSeed.Difficulty;
        persona.InitialMood = personaSeed.InitialMood;
        persona.InitialPatience = personaSeed.InitialPatience;

        foreach (var seed in requirements)
        {
            var requirement = await db.HiddenRequirements
                .FirstOrDefaultAsync(r => r.Id == seed.Id);

            if (requirement is null)
            {
                requirement = new HiddenRequirement
                {
                    Id = seed.Id,
                    ScenarioId = scenario.Id,
                    CreatedAt = DateTime.UtcNow
                };
                db.HiddenRequirements.Add(requirement);
            }

            requirement.RequirementText = seed.RequirementText;
            requirement.Category = seed.Category;
            requirement.RevealDifficulty = seed.RevealDifficulty;
            requirement.RevealCondition = seed.RevealCondition;
            requirement.GateOrder = seed.GateOrder;
        }
    }

    private static readonly HiddenRequirementSeed[] HiddenRequirementSeeds =
    [
        new(
            Guid.Parse("30000000-0000-4000-8000-000000000001"),
            "Sinh viên phải có khả năng đăng ký các học phần trực tuyến.",
            RequirementCategory.Functional,
            PersonaDifficulty.Easy,
            "Có thể mở khóa khi sinh viên hỏi về mục tiêu hệ thống hoặc quy trình đăng ký hiện tại.",
            0),
        new(
            Guid.Parse("30000000-0000-4000-8000-000000000002"),
            "Hệ thống phải bắt buộc kiểm tra điều kiện tiên quyết trước khi cho phép đăng ký.",
            RequirementCategory.Functional,
            PersonaDifficulty.Medium,
            "Mở khóa khi sinh viên hỏi về các quy tắc điều kiện, môn tiên quyết hoặc đối tượng được phép đăng ký.",
            1),
        new(
            Guid.Parse("30000000-0000-4000-8000-000000000003"),
            "Đăng ký học phần phải đóng lại trước khi học kỳ bắt đầu hai tuần.",
            RequirementCategory.BusinessRule,
            PersonaDifficulty.Medium,
            "Mở khóa khi sinh viên hỏi về hạn chót, lịch trình đăng ký hoặc các quy tắc về thời gian.",
            1),
        new(
            Guid.Parse("30000000-0000-4000-8000-000000000004"),
            "Hệ thống phải hỗ trợ ít nhất 500 người dùng truy cập đồng thời trong thời gian cao điểm đăng ký.",
            RequirementCategory.NonFunctional,
            PersonaDifficulty.Hard,
            "Chỉ mở khóa khi sinh viên hỏi trực tiếp về tải hệ thống, hiệu năng hoặc giai đoạn cao điểm.",
            4),
        new(
            Guid.Parse("30000000-0000-4000-8000-000000000005"),
            "Giảng viên có quyền miễn trừ môn tiên quyết cho sinh viên cụ thể trong các trường hợp được phê duyệt.",
            RequirementCategory.Functional,
            PersonaDifficulty.Hard,
            "Mở khóa khi sinh viên hỏi về các ngoại lệ, trường hợp đặc biệt hoặc thẩm quyền phê duyệt thủ công.",
            3),
        new(
            Guid.Parse("30000000-0000-4000-8000-000000000006"),
            "Hệ thống phải tích hợp với hệ thống tài chính hiện có để tính toán các khoản phí liên quan đến đăng ký.",
            RequirementCategory.Functional,
            PersonaDifficulty.Medium,
            "Mở khóa khi sinh viên hỏi về học phí, phụ thuộc thanh toán hoặc các hệ thống hiện có.",
            2),
        new(
            Guid.Parse("30000000-0000-4000-8000-000000000007"),
            "Hệ thống phải cung cấp danh sách chờ (wait-list) khi một lớp học phần đã đầy sinh viên.",
            RequirementCategory.Functional,
            PersonaDifficulty.Medium,
            "Mở khóa khi sinh viên hỏi về trường hợp lớp học phần bị đầy.",
            1),
        new(
            Guid.Parse("30000000-0000-4000-8000-000000000008"),
            "Sinh viên chưa hoàn tất học phí phải bị chặn đăng ký cho đến khi thanh toán xong dư nợ.",
            RequirementCategory.Constraint,
            PersonaDifficulty.Hard,
            "Mở khóa khi sinh viên hỏi về việc bị chặn đăng ký hoặc tạm khóa tài chính sau khi nhắc tới chủ đề học phí.",
            2),
        new(
            Guid.Parse("30000000-0000-4000-8000-000000000009"),
            "Hệ thống nên đáp ứng tiêu chuẩn hỗ trợ truy cập WCAG 2.1 AA cho sinh viên khuyết tật.",
            RequirementCategory.NonFunctional,
            PersonaDifficulty.Hard,
            "Chỉ mở khóa khi sinh viên hỏi về khả năng truy cập hoặc hỗ trợ cho sinh viên khuyết tật.",
            4),
        new(
            Guid.Parse("30000000-0000-4000-8000-000000000010"),
            "Sinh viên trong diện cảnh báo học tập phải có sự đồng ý của cố vấn học tập trước khi đăng ký.",
            RequirementCategory.BusinessRule,
            PersonaDifficulty.Hard,
            "Mở khóa khi sinh viên hỏi về trạng thái sinh viên đặc biệt, cảnh báo học tập hoặc quy trình duyệt của cố vấn.",
            3)
    ];

    private static readonly ScenarioSeed HospitalScenario = new(
        Guid.Parse("44444444-4444-4444-8444-444444444444"),
        "hospital_appointment",
        "Hospital Appointment System",
        "CityCare Clinic wants an online appointment booking system to reduce front-desk workload and help patients manage appointments.",
        "Healthcare",
        PersonaDifficulty.Hard,
        1);

    private static readonly PersonaSeed HospitalPersona = new(
        Guid.Parse("55555555-5555-4555-8555-555555555555"),
        "Ms. Tran",
        "Clinic Operations Coordinator",
        """{"traits":["rushed","clinical","risk_aware"],"jargon_level":"medium","cooperation_level":"medium","disclosure_style":"progressive","taboo_topics":["diagnosis","medical advice"]}""",
        "rushed-clinical",
        "high",
        PersonaDifficulty.Hard,
        "rushed",
        0.55m);

    private static readonly HiddenRequirementSeed[] HospitalRequirements =
    [
        new(Guid.Parse("60000000-0000-4000-8000-000000000001"), "Patients must be able to book appointments online.", RequirementCategory.Functional, PersonaDifficulty.Easy, "General question about online appointment booking.", 0),
        new(Guid.Parse("60000000-0000-4000-8000-000000000002"), "The system must show available doctor time slots before confirming an appointment.", RequirementCategory.Functional, PersonaDifficulty.Medium, "Question about doctor availability or appointment time slots.", 1),
        new(Guid.Parse("60000000-0000-4000-8000-000000000003"), "Urgent symptoms must trigger guidance to contact emergency services instead of booking a routine appointment.", RequirementCategory.BusinessRule, PersonaDifficulty.Hard, "Question about urgent symptoms or emergency cases.", 3),
        new(Guid.Parse("60000000-0000-4000-8000-000000000004"), "Patients can reschedule or cancel appointments up to 24 hours before the appointment time.", RequirementCategory.BusinessRule, PersonaDifficulty.Medium, "Question about changing or canceling appointments.", 1),
        new(Guid.Parse("60000000-0000-4000-8000-000000000005"), "The system should send appointment reminders by SMS or email.", RequirementCategory.Functional, PersonaDifficulty.Easy, "Question about reminders or reducing no-shows.", 1),
        new(Guid.Parse("60000000-0000-4000-8000-000000000006"), "The system must integrate with the existing EHR to retrieve patient demographics.", RequirementCategory.Functional, PersonaDifficulty.Medium, "Question about patient data or existing clinical systems.", 2),
        new(Guid.Parse("60000000-0000-4000-8000-000000000007"), "The system should support insurance eligibility checks before appointment confirmation.", RequirementCategory.Functional, PersonaDifficulty.Hard, "Question about insurance or eligibility before confirmation.", 2),
        new(Guid.Parse("60000000-0000-4000-8000-000000000008"), "Doctors must be able to block unavailable time slots.", RequirementCategory.Functional, PersonaDifficulty.Medium, "Question about doctor calendars or unavailable periods.", 1),
        new(Guid.Parse("60000000-0000-4000-8000-000000000009"), "Access to patient appointment data must be role-based.", RequirementCategory.Constraint, PersonaDifficulty.Hard, "Question about privacy, roles, or patient data access.", 4),
        new(Guid.Parse("60000000-0000-4000-8000-000000000010"), "Appointment changes must be auditable with timestamp and staff/user identity.", RequirementCategory.Constraint, PersonaDifficulty.Hard, "Question about tracking appointment changes.", 4),
        new(Guid.Parse("60000000-0000-4000-8000-000000000011"), "The system should support both Vietnamese and English patient-facing screens.", RequirementCategory.NonFunctional, PersonaDifficulty.Medium, "Question about language support or patient diversity.", 4),
        new(Guid.Parse("60000000-0000-4000-8000-000000000012"), "The system must support at least 200 concurrent booking requests during morning peak hours.", RequirementCategory.NonFunctional, PersonaDifficulty.Hard, "Question about peak load or performance.", 4)
    ];

    private static readonly ScenarioSeed InventoryScenario = new(
        Guid.Parse("77777777-7777-4777-8777-777777777777"),
        "small_business_inventory",
        "Small Business Inventory Management",
        "A small retail shop wants to replace manual stock notes and spreadsheets with a simple inventory management system.",
        "Retail",
        PersonaDifficulty.Easy,
        1);

    private static readonly PersonaSeed InventoryPersona = new(
        Guid.Parse("88888888-8888-4888-8888-888888888888"),
        "Mr. Lam",
        "Shop Owner",
        """{"traits":["friendly","practical","cost_conscious"],"jargon_level":"low","cooperation_level":"high","disclosure_style":"concrete_examples","taboo_topics":["complex enterprise architecture"]}""",
        "friendly-practical",
        "medium",
        PersonaDifficulty.Easy,
        "cooperative",
        0.80m);

    private static readonly HiddenRequirementSeed[] InventoryRequirements =
    [
        new(Guid.Parse("90000000-0000-4000-8000-000000000001"), "Staff must be able to view current stock levels for each product.", RequirementCategory.Functional, PersonaDifficulty.Easy, "General question about inventory tracking.", 0),
        new(Guid.Parse("90000000-0000-4000-8000-000000000002"), "Staff must be able to record stock-in and stock-out transactions.", RequirementCategory.Functional, PersonaDifficulty.Easy, "Question about receiving or removing stock.", 1),
        new(Guid.Parse("90000000-0000-4000-8000-000000000003"), "The system should alert the owner when product stock falls below a configurable threshold.", RequirementCategory.Functional, PersonaDifficulty.Medium, "Question about low stock or reorder alerts.", 1),
        new(Guid.Parse("90000000-0000-4000-8000-000000000004"), "The system should support barcode scanning for faster product lookup.", RequirementCategory.Functional, PersonaDifficulty.Medium, "Question about faster product lookup or barcode scanning.", 2),
        new(Guid.Parse("90000000-0000-4000-8000-000000000005"), "The system must support creating purchase orders for suppliers.", RequirementCategory.Functional, PersonaDifficulty.Medium, "Question about suppliers or reordering stock.", 2),
        new(Guid.Parse("90000000-0000-4000-8000-000000000006"), "The inventory system should integrate with the existing sales system to reduce manual updates.", RequirementCategory.Functional, PersonaDifficulty.Hard, "Question about sales/POS integration.", 2),
        new(Guid.Parse("90000000-0000-4000-8000-000000000007"), "Only the owner can edit product cost prices and supplier information.", RequirementCategory.Constraint, PersonaDifficulty.Medium, "Question about permission or sensitive inventory data.", 3),
        new(Guid.Parse("90000000-0000-4000-8000-000000000008"), "The system should generate daily stock movement and low-stock reports.", RequirementCategory.Functional, PersonaDifficulty.Medium, "Question about daily reporting.", 3),
        new(Guid.Parse("90000000-0000-4000-8000-000000000009"), "The system should continue recording transactions during temporary internet outages and sync later.", RequirementCategory.NonFunctional, PersonaDifficulty.Hard, "Question about offline use or internet outages.", 4)
    ];
}
