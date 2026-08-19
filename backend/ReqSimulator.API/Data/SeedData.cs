using Microsoft.EntityFrameworkCore;
using ReqSimulator.API.Models;

namespace ReqSimulator.API.Data;

public static class SeedData
{
    private const string ScenarioTitle = "University Course Registration System";
    private const string ScenarioKey = "university_course_registration";

    /// <summary>
    /// Ensures the published student catalog exists after every deployment.
    /// This is intentionally separate from the optional legacy sample-data seed.
    /// </summary>
    public static async Task EnsureStudentCatalogAsync(this IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var scenarioIds = await ScenarioCatalogSeeder.SeedAdditionalAsync(db, logger);
        await EnsureStakeholderPersonas(db, scenarioIds);
        await ScenarioCatalogSeeder.RetireLegacyScenariosAsync(db);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Ensured {ScenarioCount} active IT scenarios in the student catalog.",
            scenarioIds.Count);
    }

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
        var additionalScenarioIds = await ScenarioCatalogSeeder.SeedAdditionalAsync(db, logger);
        await EnsureStakeholderPersonas(
            db,
            new[] { scenario.Id, HospitalScenario.Id, InventoryScenario.Id }
                .Concat(additionalScenarioIds)
                .ToArray());
        await ScenarioCatalogSeeder.RetireLegacyScenariosAsync(db);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Seeded {ScenarioCount} active IT catalog scenarios; legacy scenarios remain available only as historical records.",
            additionalScenarioIds.Count);
    }

    private static async Task EnsureStakeholderPersonas(
        AppDbContext db,
        IReadOnlyList<Guid> scenarioIds)
    {
        var templates = new[]
        {
            ("Chủ doanh nghiệp", "Người ra quyết định", "Quản lý"),
            ("Chuyên gia Quy trình", "Chuyên viên Nghiệp vụ", "Vận hành"),
            ("Người dùng cuối", "Người dùng Trực tiếp", "Thực thi")
        };
        foreach (var scenarioId in scenarioIds)
        {
            if (await db.Stakeholders.AnyAsync(item => item.ScenarioId == scenarioId) ||
                db.Stakeholders.Local.Any(item => item.ScenarioId == scenarioId))
                continue;
            foreach (var role in templates)
            {
                var roleProfile = GetPersonaRoleProfile(role.Item2, role.Item1);
                var stakeholder = new Stakeholder
                {
                    Id = Guid.NewGuid(), ScenarioId = scenarioId, Name = role.Item1,
                    RoleTitle = role.Item2, Department = role.Item3,
                    Description = $"Đại diện cho góc nhìn của khối {role.Item3}."
                };
                db.Stakeholders.Add(stakeholder);
                foreach (var profile in new[]
                {
                    ("Hợp tác", "collaborative", PersonaDifficulty.Easy, 1.00m),
                    ("Khó tính", "concise", PersonaDifficulty.Hard, 0.70m)
                })
                {
                    db.Personas.Add(new Persona
                    {
                        Id = Guid.NewGuid(), ScenarioId = scenarioId,
                        StakeholderId = stakeholder.Id,
                        Name = $"{role.Item1} - {profile.Item1}", Label = profile.Item1,
                        RoleTitle = role.Item2,
                        PersonalityTraits = roleProfile.Traits,
                        CommunicationStyle = profile.Item2, KnowledgeLevel = roleProfile.KnowledgeLevel,
                        Difficulty = profile.Item3, InitialMood = "neutral",
                        InitialPatience = profile.Item4
                    });
                }
            }
        }

        // Existing deployments already contain these personas. Normalize them on
        // startup too, so role boundaries take effect without a data reset.
        var existingPersonas = await db.Personas
            .Include(persona => persona.Stakeholder)
            .Where(persona => scenarioIds.Contains(persona.ScenarioId))
            .ToListAsync();
        foreach (var persona in existingPersonas)
        {
            var roleProfile = GetPersonaRoleProfile(persona.RoleTitle, persona.Name);
            persona.PersonalityTraits = roleProfile.Traits;
            persona.KnowledgeLevel = roleProfile.KnowledgeLevel;
        }
    }

    private static (string Traits, string KnowledgeLevel) GetPersonaRoleProfile(
        string? roleTitle,
        string? personaName)
    {
        var identity = $"{roleTitle} {personaName}".ToLowerInvariant();
        if (identity.Contains("người dùng"))
        {
            return ("""{"traits":["practical","experience_based"],"jargon_level":"low","technical_scope":"none"}""", "low");
        }

        if (identity.Contains("chuyên viên") || identity.Contains("quy trình") || identity.Contains("nghiệp vụ"))
        {
            return ("""{"traits":["process_oriented","detail_oriented"],"jargon_level":"medium","technical_scope":"business_only"}""", "high");
        }

        return ("""{"traits":["outcome_oriented","risk_aware"],"jargon_level":"low","technical_scope":"decision_only"}""", "medium");
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
        "Hệ thống Đặt lịch khám Bệnh viện",
        "Phòng khám CityCare cần một hệ thống đặt lịch trực tuyến để giảm tải công việc lễ tân và giúp bệnh nhân quản lý lịch hẹn.",
        "Y tế",
        PersonaDifficulty.Hard,
        1);

    private static readonly PersonaSeed HospitalPersona = new(
        Guid.Parse("55555555-5555-4555-8555-555555555555"),
        "Ms. Tran",
        "Điều phối viên Vận hành",
        """{"traits":["rushed","clinical","risk_aware"],"jargon_level":"medium","cooperation_level":"medium","disclosure_style":"progressive","taboo_topics":["diagnosis","medical advice"]}""",
        "rushed-clinical",
        "high",
        PersonaDifficulty.Hard,
        "rushed",
        0.55m);

    private static readonly HiddenRequirementSeed[] HospitalRequirements =
    [
        new(Guid.Parse("60000000-0000-4000-8000-000000000001"), "Bệnh nhân phải có khả năng đặt lịch khám trực tuyến.", RequirementCategory.Functional, PersonaDifficulty.Easy, "Câu hỏi chung về đặt lịch khám trực tuyến.", 0),
        new(Guid.Parse("60000000-0000-4000-8000-000000000002"), "Hệ thống phải hiển thị các khung giờ khám bệnh của bác sĩ trước khi xác nhận lịch hẹn.", RequirementCategory.Functional, PersonaDifficulty.Medium, "Câu hỏi về sự sẵn có của bác sĩ hoặc các khung giờ khám.", 1),
        new(Guid.Parse("60000000-0000-4000-8000-000000000003"), "Các triệu chứng khẩn cấp phải kích hoạt hướng dẫn liên hệ dịch vụ cấp cứu thay vì đặt lịch khám thông thường.", RequirementCategory.BusinessRule, PersonaDifficulty.Hard, "Câu hỏi về các triệu chứng khẩn cấp hoặc trường hợp cấp cứu.", 3),
        new(Guid.Parse("60000000-0000-4000-8000-000000000004"), "Bệnh nhân có thể đổi hoặc hủy lịch hẹn trước thời gian hẹn ít nhất 24 giờ.", RequirementCategory.BusinessRule, PersonaDifficulty.Medium, "Câu hỏi về việc thay đổi hoặc hủy lịch hẹn.", 1),
        new(Guid.Parse("60000000-0000-4000-8000-000000000005"), "Hệ thống nên gửi nhắc nhở lịch hẹn qua SMS hoặc email.", RequirementCategory.Functional, PersonaDifficulty.Easy, "Câu hỏi về nhắc nhở hoặc giảm tỷ lệ bỏ lỡ cuộc hẹn.", 1),
        new(Guid.Parse("60000000-0000-4000-8000-000000000006"), "Hệ thống phải tích hợp với EHR hiện tại để truy xuất dữ liệu nhân khẩu học của bệnh nhân.", RequirementCategory.Functional, PersonaDifficulty.Medium, "Câu hỏi về dữ liệu bệnh nhân hoặc các hệ thống lâm sàng hiện có.", 2),
        new(Guid.Parse("60000000-0000-4000-8000-000000000007"), "Hệ thống nên hỗ trợ kiểm tra điều kiện bảo hiểm trước khi xác nhận lịch hẹn.", RequirementCategory.Functional, PersonaDifficulty.Hard, "Câu hỏi về bảo hiểm hoặc điều kiện trước khi xác nhận.", 2),
        new(Guid.Parse("60000000-0000-4000-8000-000000000008"), "Bác sĩ phải có khả năng chặn các khung giờ không làm việc.", RequirementCategory.Functional, PersonaDifficulty.Medium, "Câu hỏi về lịch trình của bác sĩ hoặc các khoảng thời gian không khả dụng.", 1),
        new(Guid.Parse("60000000-0000-4000-8000-000000000009"), "Quyền truy cập vào dữ liệu lịch hẹn của bệnh nhân phải dựa trên vai trò.", RequirementCategory.Constraint, PersonaDifficulty.Hard, "Câu hỏi về quyền riêng tư, vai trò hoặc truy cập dữ liệu bệnh nhân.", 4),
        new(Guid.Parse("60000000-0000-4000-8000-000000000010"), "Các thay đổi lịch hẹn phải được lưu vết cùng với mốc thời gian và danh tính nhân viên/người dùng.", RequirementCategory.Constraint, PersonaDifficulty.Hard, "Câu hỏi về theo dõi thay đổi lịch hẹn.", 4),
        new(Guid.Parse("60000000-0000-4000-8000-000000000011"), "Hệ thống nên hỗ trợ giao diện bệnh nhân bằng cả tiếng Việt và tiếng Anh.", RequirementCategory.NonFunctional, PersonaDifficulty.Medium, "Câu hỏi về hỗ trợ ngôn ngữ hoặc sự đa dạng của bệnh nhân.", 4),
        new(Guid.Parse("60000000-0000-4000-8000-000000000012"), "Hệ thống phải hỗ trợ ít nhất 200 yêu cầu đặt lịch đồng thời trong giờ cao điểm buổi sáng.", RequirementCategory.NonFunctional, PersonaDifficulty.Hard, "Câu hỏi về tải cao điểm hoặc hiệu năng.", 4)
    ];

    private static readonly ScenarioSeed InventoryScenario = new(
        Guid.Parse("77777777-7777-4777-8777-777777777777"),
        "small_business_inventory",
        "Hệ thống Quản lý Kho Cửa hàng nhỏ",
        "Một cửa hàng bán lẻ nhỏ muốn thay thế việc ghi chép sổ sách và bảng tính Excel bằng một hệ thống quản lý kho đơn giản.",
        "Bán lẻ",
        PersonaDifficulty.Easy,
        1);

    private static readonly PersonaSeed InventoryPersona = new(
        Guid.Parse("88888888-8888-4888-8888-888888888888"),
        "Mr. Lam",
        "Chủ cửa hàng",
        """{"traits":["friendly","practical","cost_conscious"],"jargon_level":"low","cooperation_level":"high","disclosure_style":"concrete_examples","taboo_topics":["complex enterprise architecture"]}""",
        "friendly-practical",
        "medium",
        PersonaDifficulty.Easy,
        "cooperative",
        0.80m);

    private static readonly HiddenRequirementSeed[] InventoryRequirements =
    [
        new(Guid.Parse("90000000-0000-4000-8000-000000000001"), "Nhân viên phải có thể xem mức tồn kho hiện tại của từng sản phẩm.", RequirementCategory.Functional, PersonaDifficulty.Easy, "Câu hỏi chung về theo dõi tồn kho.", 0),
        new(Guid.Parse("90000000-0000-4000-8000-000000000002"), "Nhân viên phải có thể ghi lại các giao dịch nhập kho và xuất kho.", RequirementCategory.Functional, PersonaDifficulty.Easy, "Câu hỏi về việc nhận hoặc xuất hàng.", 1),
        new(Guid.Parse("90000000-0000-4000-8000-000000000003"), "Hệ thống nên cảnh báo chủ cửa hàng khi lượng tồn kho sản phẩm xuống dưới mức cấu hình.", RequirementCategory.Functional, PersonaDifficulty.Medium, "Câu hỏi về cảnh báo tồn kho thấp hoặc nhắc nhở đặt hàng.", 1),
        new(Guid.Parse("90000000-0000-4000-8000-000000000004"), "Hệ thống nên hỗ trợ quét mã vạch để tra cứu sản phẩm nhanh hơn.", RequirementCategory.Functional, PersonaDifficulty.Medium, "Câu hỏi về việc tìm kiếm sản phẩm nhanh hơn hoặc quét mã vạch.", 2),
        new(Guid.Parse("90000000-0000-4000-8000-000000000005"), "Hệ thống phải hỗ trợ tạo đơn đặt hàng mua (purchase orders) cho nhà cung cấp.", RequirementCategory.Functional, PersonaDifficulty.Medium, "Câu hỏi về nhà cung cấp hoặc việc đặt lại hàng.", 2),
        new(Guid.Parse("90000000-0000-4000-8000-000000000006"), "Hệ thống kho nên tích hợp với hệ thống bán hàng hiện tại để giảm bớt việc cập nhật thủ công.", RequirementCategory.Functional, PersonaDifficulty.Hard, "Câu hỏi về tích hợp hệ thống bán hàng/POS.", 2),
        new(Guid.Parse("90000000-0000-4000-8000-000000000007"), "Chỉ chủ cửa hàng mới có thể chỉnh sửa giá vốn sản phẩm và thông tin nhà cung cấp.", RequirementCategory.Constraint, PersonaDifficulty.Medium, "Câu hỏi về quyền hạn hoặc dữ liệu kho nhạy cảm.", 3),
        new(Guid.Parse("90000000-0000-4000-8000-000000000008"), "Hệ thống nên tạo báo cáo biến động tồn kho và hàng tồn thấp hàng ngày.", RequirementCategory.Functional, PersonaDifficulty.Medium, "Câu hỏi về báo cáo hàng ngày.", 3),
        new(Guid.Parse("90000000-0000-4000-8000-000000000009"), "Hệ thống nên tiếp tục ghi lại các giao dịch trong thời gian mất kết nối internet và đồng bộ lại sau.", RequirementCategory.NonFunctional, PersonaDifficulty.Hard, "Câu hỏi về sử dụng ngoại tuyến hoặc sự cố internet.", 4)
    ];
}
