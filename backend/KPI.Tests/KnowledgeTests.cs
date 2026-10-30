using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KPI.Application.Knowledge;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Domain.Exceptions;
using KPI.Infrastructure.Data;
using KPI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace KPI.Tests;

public class KnowledgeTests
{
    private static SaveArticleRequest Article(string title = "ตั้งค่า VPN บนโน้ตบุ๊ก", string summary = "ขั้นตอนต่อ VPN จากที่บ้าน",
        string body = "1. เปิดโปรแกรม\n\n2. กดเชื่อมต่อ", string? category = "เครือข่าย", int? version = null) =>
        new(title, summary, body, category, version);

    private static async Task<ArticleDto> PublishedAsync(TestWorld w, SaveArticleRequest request)
    {
        var draft = await w.Knowledge.CreateAsync(request, w.ItA.Id);
        return await w.Knowledge.PublishAsync(draft.Id, w.ItA.Id);
    }

    private static Task<KPI.Application.Common.PagedResult<ArticleListItemDto>> ListAsync(TestWorld w, User by, KnowledgeQuery? query = null) =>
        w.Knowledge.ListAsync(query ?? new KnowledgeQuery(), by.Id);

    // ───────────── Draft and publish ─────────────

    [Fact]
    public async Task A_draft_is_not_found_for_a_reader_and_shown_to_a_manager()
    {
        var w = await TestWorld.CreateAsync();
        var draft = await w.Knowledge.CreateAsync(Article(), w.ItA.Id);

        Assert.False(draft.IsPublished);
        Assert.Null(draft.PublishedAt);
        await Assert.ThrowsAsync<NotFoundException>(() => w.Knowledge.GetAsync(draft.Id, w.StaffA.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => w.Knowledge.GetAsync(draft.Id, w.ManagerA.Id));
        Assert.Empty((await ListAsync(w, w.StaffA)).Items);
        Assert.Empty(await w.Knowledge.CategoriesAsync(w.StaffA.Id));

        Assert.Equal(draft.Id, (await w.Knowledge.GetAsync(draft.Id, w.ItA.Id)).Id);
        Assert.Equal(draft.Id, Assert.Single((await ListAsync(w, w.ItA)).Items).Id);
        Assert.Equal(["เครือข่าย"], await w.Knowledge.CategoriesAsync(w.ItA.Id));
    }

    [Fact]
    public async Task A_reader_cannot_ask_for_drafts_through_the_status_filter()
    {
        var w = await TestWorld.CreateAsync();
        await w.Knowledge.CreateAsync(Article("ฉบับร่างเรื่องเครื่องพิมพ์"), w.ItA.Id);
        await PublishedAsync(w, Article("เครื่องพิมพ์ต่อไม่ติด"));

        Assert.Single((await ListAsync(w, w.StaffA, new KnowledgeQuery(Published: false))).Items);   // the published one
        Assert.All((await ListAsync(w, w.StaffA, new KnowledgeQuery(Published: false))).Items, a => Assert.True(a.IsPublished));
        Assert.Equal(2, (await ListAsync(w, w.ItA)).Total);
        Assert.Equal("ฉบับร่างเรื่องเครื่องพิมพ์", Assert.Single((await ListAsync(w, w.ItA, new KnowledgeQuery(Published: false))).Items).Title);
        Assert.Equal("เครื่องพิมพ์ต่อไม่ติด", Assert.Single((await ListAsync(w, w.ItA, new KnowledgeQuery(Published: true))).Items).Title);
    }

    [Fact]
    public async Task IT_runs_the_whole_lifecycle_and_every_step_is_audited()
    {
        var w = await TestWorld.CreateAsync();
        var draft = await w.Knowledge.CreateAsync(Article(), w.ItA.Id);
        Assert.Equal(0, draft.Version);
        Assert.Equal(w.ItA.FullName, draft.AuthorName);

        var edited = await w.Knowledge.UpdateAsync(draft.Id, Article("ตั้งค่า VPN ใหม่", version: draft.Version), w.ItA.Id);
        Assert.Equal("ตั้งค่า VPN ใหม่", edited.Title);
        Assert.Equal(1, edited.Version);

        var live = await w.Knowledge.PublishAsync(draft.Id, w.ItA.Id);
        Assert.True(live.IsPublished);
        Assert.Equal(w.Clock.UtcNow, live.PublishedAt);
        Assert.Equal(edited.Version + 1, live.Version);
        Assert.Equal(live.Id, (await w.Knowledge.GetAsync(draft.Id, w.StaffA.Id)).Id);
        await Assert.ThrowsAsync<InvalidStateException>(() => w.Knowledge.PublishAsync(draft.Id, w.ItA.Id));

        var hidden = await w.Knowledge.UnpublishAsync(draft.Id, w.ItA.Id);
        Assert.False(hidden.IsPublished);
        Assert.Null(hidden.PublishedAt);
        await Assert.ThrowsAsync<NotFoundException>(() => w.Knowledge.GetAsync(draft.Id, w.StaffA.Id));
        await Assert.ThrowsAsync<InvalidStateException>(() => w.Knowledge.UnpublishAsync(draft.Id, w.ItA.Id));

        await w.Knowledge.DeleteAsync(draft.Id, w.ItA.Id);
        Assert.Empty((await ListAsync(w, w.ItA)).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => w.Knowledge.GetAsync(draft.Id, w.ItA.Id));

        var audit = await w.Db.AuditLogs.Where(a => a.EntityName == "KnowledgeArticle" && a.EntityId == draft.Id.ToString()).ToListAsync();
        Assert.Equivalent(new[] { "Create", "Update", "Publish", "Unpublish", "Delete" }, audit.Select(a => a.Action).ToArray());
        Assert.All(audit, a => Assert.Equal(w.ItA.Id, a.UserId));
        Assert.Equal("ตั้งค่า VPN บนโน้ตบุ๊ก", audit.Single(a => a.Action == "Update").OldValues);
        Assert.Equal("ตั้งค่า VPN บนโน้ตบุ๊ก", audit.Single(a => a.Action == "Create").NewValues);
        Assert.DoesNotContain(audit, a => (a.OldValues ?? "").Contains("เปิดโปรแกรม") || (a.NewValues ?? "").Contains("เปิดโปรแกรม"));
    }

    [Fact]
    public async Task Editing_a_published_article_keeps_it_published()
    {
        var w = await TestWorld.CreateAsync();
        var live = await PublishedAsync(w, Article());
        w.Clock.Advance(TimeSpan.FromHours(1));

        var edited = await w.Knowledge.UpdateAsync(live.Id, Article(body: "ขั้นตอนใหม่", version: live.Version), w.SuperAdmin.Id);

        Assert.True(edited.IsPublished);
        Assert.Equal(live.PublishedAt, edited.PublishedAt);
        Assert.Equal(w.SuperAdmin.FullName, edited.AuthorName);
        Assert.Equal("ขั้นตอนใหม่", (await w.Knowledge.GetAsync(live.Id, w.StaffA.Id)).Body);
    }

    // ───────────── Who may write ─────────────

    [Fact]
    public async Task Everyone_without_the_manage_right_is_refused_every_write()
    {
        var w = await TestWorld.CreateAsync();
        var live = await PublishedAsync(w, Article());

        foreach (var user in new[] { w.StaffA, w.ManagerA, w.Director, w.ViewerA, w.TeamLeaderA })
        {
            await Assert.ThrowsAsync<ForbiddenException>(() => w.Knowledge.CreateAsync(Article(), user.Id));
            await Assert.ThrowsAsync<ForbiddenException>(() => w.Knowledge.UpdateAsync(live.Id, Article(version: live.Version), user.Id));
            await Assert.ThrowsAsync<ForbiddenException>(() => w.Knowledge.PublishAsync(live.Id, user.Id));
            await Assert.ThrowsAsync<ForbiddenException>(() => w.Knowledge.UnpublishAsync(live.Id, user.Id));
            await Assert.ThrowsAsync<ForbiddenException>(() => w.Knowledge.DeleteAsync(live.Id, user.Id));
        }

        Assert.True((await w.Knowledge.GetAsync(live.Id, w.ItA.Id, countView: false)).IsPublished);
    }

    [Fact]
    public async Task A_deactivated_account_is_refused_even_for_reading()
    {
        var w = await TestWorld.CreateAsync();
        var live = await PublishedAsync(w, Article());
        var gone = await w.AddUserAsync("gone", RoleNames.ITSupport, w.DeptB.Id, active: false);

        await Assert.ThrowsAsync<ForbiddenException>(() => w.Knowledge.GetAsync(live.Id, gone.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Knowledge.CreateAsync(Article(), gone.Id));
    }

    // ───────────── Version and validation ─────────────

    [Fact]
    public async Task Saving_with_an_old_version_is_a_conflict_and_changes_nothing()
    {
        var w = await TestWorld.CreateAsync();
        var draft = await w.Knowledge.CreateAsync(Article(), w.ItA.Id);
        await w.Knowledge.UpdateAsync(draft.Id, Article("ฉบับของคนแรก", version: draft.Version), w.ItA.Id);

        await Assert.ThrowsAsync<InvalidStateException>(() =>
            w.Knowledge.UpdateAsync(draft.Id, Article("ฉบับของคนที่สอง", version: draft.Version), w.ItB.Id));
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            w.Knowledge.UpdateAsync(draft.Id, Article("ไม่มีเวอร์ชัน", version: null), w.ItB.Id));

        Assert.Equal("ฉบับของคนแรก", (await w.Knowledge.GetAsync(draft.Id, w.ItA.Id)).Title);
    }

    [Fact]
    public async Task Publishing_makes_an_editor_that_loaded_earlier_conflict()
    {
        var w = await TestWorld.CreateAsync();
        var draft = await w.Knowledge.CreateAsync(Article(), w.ItA.Id);
        await w.Knowledge.PublishAsync(draft.Id, w.ItB.Id);

        await Assert.ThrowsAsync<InvalidStateException>(() =>
            w.Knowledge.UpdateAsync(draft.Id, Article("แก้ไขจากหน้าที่เปิดค้างไว้", version: draft.Version), w.ItA.Id));
    }

    [Theory]
    [InlineData("ab", "ok", "body", "cat")]                            // title too short
    [InlineData("   ", "ok", "body", "cat")]                           // title blank
    [InlineData(null, "ok", "body", "cat")]
    [InlineData("title", "ok", "   ", "cat")]                          // body blank
    [InlineData("title", "ok", null, "cat")]
    public async Task Missing_text_is_refused(string? title, string? summary, string? body, string? category)
    {
        var w = await TestWorld.CreateAsync();
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            w.Knowledge.CreateAsync(new SaveArticleRequest(title, summary, body, category), w.ItA.Id));
        Assert.Empty((await ListAsync(w, w.ItA)).Items);
    }

    [Fact]
    public async Task The_length_limits_hold_at_the_edge_and_refuse_one_over()
    {
        var w = await TestWorld.CreateAsync();

        var longest = await w.Knowledge.CreateAsync(new SaveArticleRequest(
            new string('t', KnowledgeService.MaxTitleLength), new string('s', KnowledgeService.MaxSummaryLength),
            new string('b', KnowledgeService.MaxBodyLength), new string('c', KnowledgeService.MaxCategoryLength)), w.ItA.Id);
        Assert.Equal(KnowledgeService.MaxBodyLength, longest.Body.Length);

        await Assert.ThrowsAsync<RequestValidationException>(() => w.Knowledge.CreateAsync(
            Article(title: new string('t', KnowledgeService.MaxTitleLength + 1)), w.ItA.Id));
        await Assert.ThrowsAsync<RequestValidationException>(() => w.Knowledge.CreateAsync(
            Article(summary: new string('s', KnowledgeService.MaxSummaryLength + 1)), w.ItA.Id));
        await Assert.ThrowsAsync<RequestValidationException>(() => w.Knowledge.CreateAsync(
            Article(body: new string('b', KnowledgeService.MaxBodyLength + 1)), w.ItA.Id));
        await Assert.ThrowsAsync<RequestValidationException>(() => w.Knowledge.CreateAsync(
            Article(category: new string('c', KnowledgeService.MaxCategoryLength + 1)), w.ItA.Id));
    }

    [Fact]
    public async Task Text_is_trimmed_and_a_blank_category_becomes_the_default()
    {
        var w = await TestWorld.CreateAsync();
        var saved = await w.Knowledge.CreateAsync(new SaveArticleRequest("  หัวข้อ  ", "  สรุป  ", "  เนื้อหา  ", "   "), w.ItA.Id);

        Assert.Equal("หัวข้อ", saved.Title);
        Assert.Equal("สรุป", saved.Summary);
        Assert.Equal("เนื้อหา", saved.Body);
        Assert.Equal(KnowledgeService.DefaultCategory, saved.Category);
    }

    // ───────────── Search ─────────────

    [Fact]
    public async Task Search_matches_title_and_summary_ignoring_case_but_not_the_body_unless_asked()
    {
        var w = await TestWorld.CreateAsync();
        await PublishedAsync(w, Article("Reset Password ใน Windows", "ลืมรหัสผ่านเครื่อง", "เปิด Settings แล้วเลือก Accounts"));
        await PublishedAsync(w, Article("จอภาพกะพริบ", "ตรวจสอบสายสัญญาณ HDMI", "ถอดสายแล้วเสียบใหม่"));

        Assert.Equal("Reset Password ใน Windows", Assert.Single((await ListAsync(w, w.StaffA, new KnowledgeQuery(Search: "RESET password"))).Items).Title);
        Assert.Equal("จอภาพกะพริบ", Assert.Single((await ListAsync(w, w.StaffA, new KnowledgeQuery(Search: "hdmi"))).Items).Title);

        // "Accounts" is only in a body
        Assert.Empty((await ListAsync(w, w.StaffA, new KnowledgeQuery(Search: "accounts"))).Items);
        Assert.Equal("Reset Password ใน Windows",
            Assert.Single((await ListAsync(w, w.StaffA, new KnowledgeQuery(Search: "accounts", InBody: true))).Items).Title);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("[a-z]")]
    [InlineData("100%")]
    public async Task Search_treats_wildcards_as_plain_text(string term)
    {
        var w = await TestWorld.CreateAsync();
        await PublishedAsync(w, Article("เครื่องพิมพ์ทั่วไป", "ไม่มีอักขระพิเศษ", "ไม่มี"));
        await PublishedAsync(w, Article("ลดต้นทุน 100% ด้วย a_b", "ตัวอย่าง [a-z]", "x"));

        var found = (await ListAsync(w, w.StaffA, new KnowledgeQuery(Search: term))).Items;

        Assert.Equal("ลดต้นทุน 100% ด้วย a_b", Assert.Single(found).Title);
    }

    [Fact]
    public async Task The_category_filter_and_paging_work_together()
    {
        var w = await TestWorld.CreateAsync();
        for (var i = 0; i < 3; i++) await PublishedAsync(w, Article($"เครือข่ายเรื่องที่ {i}", category: "เครือข่าย"));
        await PublishedAsync(w, Article("อีเมล", category: "อีเมล"));

        var page = await ListAsync(w, w.StaffA, new KnowledgeQuery(Category: "เครือข่าย", PageSize: 2));

        Assert.Equal(3, page.Total);
        Assert.Equal(2, page.Items.Count);
        Assert.Equivalent(new[] { "อีเมล", "เครือข่าย" }, (await w.Knowledge.CategoriesAsync(w.StaffA.Id)).ToArray());
    }

    // ───────────── Suggestions ─────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("vp")]
    [InlineData(" vp ")]
    public async Task A_title_under_three_characters_suggests_nothing(string? title)
    {
        var w = await TestWorld.CreateAsync();
        await PublishedAsync(w, Article("VPN ต่อไม่ได้"));

        Assert.Empty(await w.Knowledge.SuggestAsync(title, w.StaffA.Id));
    }

    [Fact]
    public async Task Suggestions_are_published_articles_best_match_first_and_at_most_five()
    {
        var w = await TestWorld.CreateAsync();
        for (var i = 0; i < 8; i++) await PublishedAsync(w, Article($"VPN ปัญหาข้อ {i}", summary: "อื่น ๆ"));
        var best = await PublishedAsync(w, Article("VPN ต่อไม่ได้ จากที่บ้าน", summary: "ตรวจสอบรหัสผ่าน"));
        await w.Knowledge.CreateAsync(Article("VPN ฉบับร่าง ต่อไม่ได้ จากที่บ้าน"), w.ItA.Id);

        var suggestions = await w.Knowledge.SuggestAsync("VPN ต่อไม่ได้ จากที่บ้าน", w.StaffA.Id);

        Assert.Equal(IKnowledgeService.MaxSuggestions, suggestions.Count);
        Assert.Equal(best.Id, suggestions[0].Id);
        Assert.DoesNotContain(suggestions, s => s.Title.Contains("ฉบับร่าง"));
    }

    [Fact]
    public async Task A_draft_is_never_suggested_even_to_a_manager()
    {
        var w = await TestWorld.CreateAsync();
        await w.Knowledge.CreateAsync(Article("เครื่องพิมพ์ไม่ทำงาน"), w.ItA.Id);

        Assert.Empty(await w.Knowledge.SuggestAsync("เครื่องพิมพ์", w.ItA.Id));
    }

    [Fact]
    public async Task A_word_in_the_summary_also_suggests()
    {
        var w = await TestWorld.CreateAsync();
        var article = await PublishedAsync(w, Article("ปัญหาการพิมพ์", summary: "แก้ไขเมื่อ printer เสีย"));

        Assert.Equal(article.Id, Assert.Single(await w.Knowledge.SuggestAsync("printer เสีย", w.StaffA.Id)).Id);
    }

    // ───────────── Views ─────────────

    [Fact]
    public async Task Only_opening_a_published_article_counts_a_view()
    {
        var w = await TestWorld.CreateAsync();
        var draft = await w.Knowledge.CreateAsync(Article(), w.ItA.Id);

        await w.Knowledge.GetAsync(draft.Id, w.ItA.Id);                      // a manager reading a draft
        Assert.Equal(0, (await w.Knowledge.GetAsync(draft.Id, w.ItA.Id)).ViewCount);

        await w.Knowledge.PublishAsync(draft.Id, w.ItA.Id);
        Assert.Equal(1, (await w.Knowledge.GetAsync(draft.Id, w.StaffA.Id)).ViewCount);
        Assert.Equal(2, (await w.Knowledge.GetAsync(draft.Id, w.StaffB.Id)).ViewCount);
        Assert.Equal(2, (await w.Knowledge.GetAsync(draft.Id, w.ItA.Id, countView: false)).ViewCount);   // the editor loading it

        w.Clear();
        Assert.Equal(2, (await ListAsync(w, w.StaffA)).Items.Single().ViewCount);
    }

    [Fact]
    public async Task A_view_does_not_change_the_version_so_an_open_editor_can_still_save()
    {
        var w = await TestWorld.CreateAsync();
        var live = await PublishedAsync(w, Article());
        await w.Knowledge.GetAsync(live.Id, w.StaffA.Id);

        var edited = await w.Knowledge.UpdateAsync(live.Id, Article("หัวข้อใหม่", version: live.Version), w.ItA.Id);

        Assert.Equal("หัวข้อใหม่", edited.Title);
        Assert.Equal(1, edited.ViewCount);
    }

    // ───────────── A right that did not exist before ─────────────

    [Fact]
    public async Task Custom_roles_inherit_the_new_rights_once_and_an_administrator_can_take_them_away()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        await DbSeeder.SeedAsync(db);

        // an older database: the two rights are not there yet
        var knowledge = await db.Permissions.Where(p => p.Name.StartsWith("knowledge.")).ToListAsync();
        var knowledgeIds = knowledge.Select(p => p.Id).ToList();
        db.RolePermissions.RemoveRange(db.RolePermissions.Where(rp => knowledgeIds.Contains(rp.PermissionId)));
        db.Permissions.RemoveRange(knowledge);
        var byName = await db.Permissions.ToDictionaryAsync(p => p.Name, p => p.Id);

        Role Custom(string name, params string[] holds)
        {
            var role = new Role { Name = name, Description = "custom" };
            foreach (var permission in holds)
                role.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = byName[permission] });
            db.Roles.Add(role);
            return role;
        }

        var helper = Custom("Helper", Permissions.Issue.View, Permissions.Issue.Manage);
        var reader = Custom("Reader", Permissions.Issue.View);
        var outsider = Custom("Outsider", Permissions.Department.View);
        await db.SaveChangesAsync();

        await DbSeeder.SyncPermissionsAsync(db);

        async Task<HashSet<string>> HeldAsync(Role role) => (await db.RolePermissions
            .Where(rp => rp.RoleId == role.Id).Select(rp => rp.Permission.Name).ToListAsync()).ToHashSet();

        Assert.Contains(Permissions.Knowledge.View, await HeldAsync(helper));
        Assert.Contains(Permissions.Knowledge.Manage, await HeldAsync(helper));
        Assert.Contains(Permissions.Knowledge.View, await HeldAsync(reader));
        Assert.DoesNotContain(Permissions.Knowledge.Manage, await HeldAsync(reader));
        Assert.DoesNotContain(Permissions.Knowledge.View, await HeldAsync(outsider));

        // the administrator takes the right away; the next start must not give it back
        db.RolePermissions.RemoveRange(db.RolePermissions.Where(rp => rp.RoleId == helper.Id && rp.Permission.Name == Permissions.Knowledge.Manage));
        await db.SaveChangesAsync();

        await DbSeeder.SyncPermissionsAsync(db);

        Assert.DoesNotContain(Permissions.Knowledge.Manage, await HeldAsync(helper));
        Assert.Contains(Permissions.Knowledge.View, await HeldAsync(helper));
    }
}

public class KnowledgeApiTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static bool _seeded;

    private readonly ApiFactory _factory;

    public KnowledgeApiTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await SeedLock.WaitAsync();
        try
        {
            if (!_seeded) { await _factory.SeedAsync(); _seeded = true; }
        }
        finally { SeedLock.Release(); }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Nothing_is_open_to_a_visitor_who_is_not_signed_in()
    {
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/knowledge")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/knowledge/suggest?title=vpn")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/knowledge/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/knowledge", new { title = "x", body = "y" })).StatusCode);
    }

    [Fact]
    public async Task A_reader_gets_forbidden_on_every_write_and_not_found_on_a_draft()
    {
        var it = await _factory.ClientAsync("it-a");
        var staff = await _factory.ClientAsync("staff-a");

        var created = await it.PostAsJsonAsync("/api/knowledge", new { title = "ฉบับร่างทดสอบ", summary = "", body = "เนื้อหา", category = "ทั่วไป" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await Json(created)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/api/knowledge/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await it.GetAsync($"/api/knowledge/{id}")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/knowledge", new { title = "ฉบับของผู้อ่าน", body = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync($"/api/knowledge/{id}", new { title = "แก้", body = "x", version = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsync($"/api/knowledge/{id}/publish", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsync($"/api/knowledge/{id}/unpublish", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.DeleteAsync($"/api/knowledge/{id}")).StatusCode);
    }

    [Fact]
    public async Task A_published_article_is_readable_and_a_stale_save_is_a_conflict()
    {
        var it = await _factory.ClientAsync("it-a");
        var staff = await _factory.ClientAsync("staff-a");

        var created = await Json(await it.PostAsJsonAsync("/api/knowledge",
            new { title = "รีเซ็ตรหัสผ่านอีเมล", summary = "วิธีรีเซ็ต", body = "ขั้นตอนที่หนึ่ง\n\nขั้นตอนที่สอง", category = "อีเมล" }));
        var id = created.GetProperty("id").GetGuid();

        var published = await it.PostAsync($"/api/knowledge/{id}/publish", null);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);

        var read = await Json(await staff.GetAsync($"/api/knowledge/{id}"));
        Assert.Equal("รีเซ็ตรหัสผ่านอีเมล", read.GetProperty("title").GetString());
        Assert.Contains((await Json(await staff.GetAsync("/api/knowledge?search=รีเซ็ต"))).GetProperty("items").EnumerateArray(),
            a => a.GetProperty("id").GetGuid() == id);
        Assert.Contains((await Json(await staff.GetAsync("/api/knowledge/suggest?title=รีเซ็ตรหัสผ่าน")))
            .EnumerateArray(), a => a.GetProperty("id").GetGuid() == id);

        // version 0 was the draft; publishing raised it
        Assert.Equal(HttpStatusCode.Conflict,
            (await it.PutAsJsonAsync($"/api/knowledge/{id}", new { title = "แก้ไขเก่า", body = "x", version = 0 })).StatusCode);
        var version = read.GetProperty("version").GetInt32();
        Assert.Equal(HttpStatusCode.OK,
            (await it.PutAsJsonAsync($"/api/knowledge/{id}", new { title = "แก้ไขใหม่", body = "x", version })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await it.DeleteAsync($"/api/knowledge/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/api/knowledge/{id}")).StatusCode);
    }
}
