using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KPI.Domain.Constants;
using KPI.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KPI.Tests;

/// <summary>The same app, reading the client address from X-Test-Ip so tests can pretend to be different machines.</summary>
public class IssueApiFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("LoginThrottle:ClientIpHeader", "X-Test-Ip");
    }
}

public class IssueApiTests : IClassFixture<IssueApiFactory>, IAsyncLifetime
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static bool _seeded;
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    private readonly IssueApiFactory _factory;

    public IssueApiTests(IssueApiFactory factory) => _factory = factory;

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

    private async Task<HttpClient> As(string user, string? ip = null)
    {
        var client = await _factory.ClientAsync(user);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IntegrationTest/1.0");
        if (ip != null) client.DefaultRequestHeaders.Add("X-Test-Ip", ip);
        return client;
    }

    private static MultipartFormDataContent ReportForm(string title = "เข้าไม่ได้เลย", bool withImage = false, string? website = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(title), "Title" },
            { new StringContent("กดปุ่มแล้วไม่มีอะไรเกิดขึ้นเลยทั้งเช้าและบ่าย"), "Description" },
            { new StringContent("Bug"), "Category" },
            { new StringContent("Application"), "Module" },
            { new StringContent("Degraded"), "Impact" },
        };
        if (website != null) form.Add(new StringContent(website), "Website");
        if (withImage)
        {
            var file = new ByteArrayContent(Png);
            file.Headers.ContentType = new("image/png");
            form.Add(file, "Files", "screen.png");
        }
        return form;
    }

    private static MultipartFormDataContent PublicForm(Guid? department, string name = "สมหญิง", string title = "เข้าสู่ระบบไม่ได้")
    {
        var form = ReportForm(title);
        form.Add(new StringContent(name), "ReporterName");
        form.Add(new StringContent("somying@example.com"), "ReporterEmail");
        if (department != null) form.Add(new StringContent(department.Value.ToString()), "DepartmentId");
        return form;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The expected record did not appear in time.");
            await Task.Delay(25);
        }
    }

    // ───────────── The public form ─────────────

    [Fact]
    public async Task Departments_are_listed_for_the_public_form_without_signing_in()
    {
        var response = await _factory.CreateClient().GetAsync("/api/public/departments");

        response.EnsureSuccessStatusCode();
        var departments = (await Json(response)).EnumerateArray().ToList();
        Assert.Contains("Dept A", departments.Select(d => d.GetProperty("name").GetString()));
        Assert.All(departments, d => Assert.Equal(2, d.EnumerateObject().Count())); // id + name only
    }

    [Fact]
    public async Task Someone_who_cannot_sign_in_can_report_and_IT_sees_it()
    {
        var response = await _factory.CreateClient().PostAsync("/api/public/issues", PublicForm(_factory.DeptA.Id, title: "ลืมรหัสผ่านแล้วรีเซ็ตไม่ได้"));

        response.EnsureSuccessStatusCode();
        var receipt = await Json(response);
        var number = receipt.GetProperty("number").GetString()!;
        Assert.StartsWith("ISS-", number);

        var it = await As("it-a");
        var list = await Json(await it.GetAsync("/api/issues?search=ลืมรหัสผ่าน"));
        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(number, item.GetProperty("number").GetString());
        Assert.True(item.GetProperty("isPublicSubmission").GetBoolean());
    }

    [Fact]
    public async Task The_hidden_honeypot_field_makes_the_report_vanish_quietly()
    {
        var before = await CountIssuesAsync();
        var form = PublicForm(null, title: "สแปมจากบอท");
        form.Add(new StringContent("http://spam.example"), "Website");

        var response = await _factory.CreateClient().PostAsync("/api/public/issues", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);      // the bot is not told
        Assert.Equal(before, await CountIssuesAsync());
    }

    [Fact]
    public async Task The_public_form_is_rate_limited_per_address()
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Ip", "198.51.100.77");
            var response = await client.PostAsync("/api/public/issues", PublicForm(null, title: $"ทดสอบการส่งซ้ำครั้งที่ {i + 1}"));
            statuses.Add(response.StatusCode);
            if (i == 3) Assert.True(response.Headers.Contains("Retry-After"));
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);

        var elsewhere = _factory.CreateClient();
        elsewhere.DefaultRequestHeaders.Add("X-Test-Ip", "198.51.100.78");
        Assert.Equal(HttpStatusCode.OK, (await elsewhere.PostAsync("/api/public/issues", PublicForm(null, title: "อีกเครื่องหนึ่ง"))).StatusCode);
    }

    [Fact]
    public async Task A_public_report_without_contact_details_is_refused_with_a_readable_message()
    {
        var form = ReportForm("ไม่มีช่องทางติดต่อ");
        form.Add(new StringContent("สมชาย"), "ReporterName");

        var response = await _factory.CreateClient().PostAsync("/api/public/issues", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("อีเมลหรือเบอร์โทร", (await Json(response)).GetProperty("message").GetString());
    }

    // ───────────── Signed-in reporting and the workflow ─────────────

    [Fact]
    public async Task A_signed_in_user_reports_with_a_screenshot_and_only_they_and_IT_can_see_it()
    {
        var staff = await As("staff-a");
        var created = await staff.PostAsync("/api/issues", ReportForm("ปุ่มอนุมัติไม่ทำงาน", withImage: true));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var issue = await Json(created);
        var id = issue.GetProperty("id").GetGuid();
        Assert.Equal("Dept A", issue.GetProperty("departmentName").GetString());
        var attachment = Assert.Single(issue.GetProperty("attachments").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, (await (await As("it-a")).GetAsync($"/api/issues/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await (await As("staff-b")).GetAsync($"/api/issues/{id}")).StatusCode);

        var image = await staff.GetAsync($"/api/issues/{id}/attachments/{attachment.GetProperty("id").GetGuid()}");
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", image.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(Png, await image.Content.ReadAsByteArrayAsync());

        var other = await (await As("staff-b")).GetAsync($"/api/issues/{id}/attachments/{attachment.GetProperty("id").GetGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
    }

    [Fact]
    public async Task A_report_that_is_not_an_image_is_refused()
    {
        var form = ReportForm("ไฟล์แปลก");
        var file = new ByteArrayContent([0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]);
        file.Headers.ContentType = new("image/png");
        form.Add(file, "Files", "virus.png");

        var response = await (await As("staff-a")).PostAsync("/api/issues", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_whole_workflow_over_HTTP_with_the_right_people_allowed_at_each_step()
    {
        var staff = await As("staff-a");
        var it = await As("it-a");
        var id = (await Json(await staff.PostAsync("/api/issues", ReportForm("ส่งออก Excel ไม่ได้")))).GetProperty("id").GetGuid();

        // a requester cannot work the issue
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsync($"/api/issues/{id}/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync($"/api/issues/{id}/resolve",
            new { resolution = "ลองดูเอง", rootCause = "UserError" })).StatusCode);

        // IT picks it up; resolving before starting is the wrong status
        Assert.Equal(HttpStatusCode.Conflict, (await it.PostAsJsonAsync($"/api/issues/{id}/resolve",
            new { resolution = "แก้แล้ว", rootCause = "CodeBug" })).StatusCode);
        var itId = (await Json(await it.GetAsync("/api/issues/assignees"))).EnumerateArray()
            .First(a => a.GetProperty("name").GetString()!.Contains("it-a")).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await it.PostAsJsonAsync($"/api/issues/{id}/assign", new { assigneeId = itId })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await it.PostAsync($"/api/issues/{id}/start", null)).StatusCode);

        // question and answer
        Assert.Equal(HttpStatusCode.OK, (await it.PostAsJsonAsync($"/api/issues/{id}/request-info", new { message = "ใช้เบราว์เซอร์อะไร" })).StatusCode);
        var answered = await Json(await staff.PostAsJsonAsync($"/api/issues/{id}/comments", new { body = "Chrome ครับ" }));
        Assert.Equal(2, answered.GetProperty("status").GetInt32());   // back to InProgress

        // fix, confirm
        var resolved = await Json(await it.PostAsJsonAsync($"/api/issues/{id}/resolve", new
        {
            resolution = "ปรับสิทธิ์การส่งออกให้ถูกต้องแล้ว",
            rootCause = "Configuration",
            prevention = "เพิ่มการตรวจสอบสิทธิ์ตอน deploy",
            timeSpentMinutes = 45,
        }));
        Assert.Equal(4, resolved.GetProperty("status").GetInt32());
        var closed = await Json(await staff.PostAsync($"/api/issues/{id}/confirm", null));
        Assert.Equal(5, closed.GetProperty("status").GetInt32());

        // finished issues refuse further work
        Assert.Equal(HttpStatusCode.Conflict, (await it.PostAsync($"/api/issues/{id}/start", null)).StatusCode);
    }

    [Fact]
    public async Task The_summary_and_assignee_endpoints_follow_the_role()
    {
        Assert.Equal(HttpStatusCode.OK, (await (await As("staff-a")).GetAsync("/api/issues/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As("staff-a")).GetAsync("/api/issues/assignees")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await As("it-a")).GetAsync("/api/issues/assignees")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/issues")).StatusCode);
    }

    // ───────────── The audit trail: who, from where ─────────────

    [Fact]
    public async Task Audit_rows_carry_who_what_role_department_and_where_from()
    {
        var staff = await As("staff-a", ip: "203.0.113.7");
        var id = (await Json(await staff.PostAsync("/api/issues", ReportForm("ตรวจสอบ audit")))).GetProperty("id").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AuditLogs.SingleAsync(a => a.EntityName == "Issue" && a.EntityId == id.ToString() && a.Action == "Create");

        Assert.Equal("203.0.113.7", row.IpAddress);
        Assert.Equal("IntegrationTest/1.0", row.UserAgent);
        Assert.Equal("staff-a@example.com", row.UserEmail);
        Assert.Contains("staff-a", row.UserName);
        Assert.Equal("Staff", row.UserRole);
        Assert.Equal("Dept A", row.DepartmentName);
        Assert.False(string.IsNullOrEmpty(row.RequestId));
    }

    [Fact]
    public async Task Writes_refusals_and_failed_sign_ins_are_logged_but_ordinary_reads_are_not()
    {
        var marker = $"198.51.100.{Random.Shared.Next(100, 200)}";
        var staff = await As("staff-a", marker);

        await staff.GetAsync("/api/issues");                                            // ordinary read: not logged
        await staff.PostAsync("/api/issues", ReportForm("บันทึกคำขอ"));                  // a write
        await staff.GetAsync("/api/users");                                             // refused (403)
        var anonymous = _factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Test-Ip", marker);
        await anonymous.PostAsJsonAsync("/api/auth/login", new { email = "Someone@Example.com", password = "wrong-password" });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await WaitUntilAsync(() => db.RequestLogs.Count(r => r.IpAddress == marker) >= 3);
        var logs = await db.RequestLogs.AsNoTracking().Where(r => r.IpAddress == marker).ToListAsync();

        Assert.DoesNotContain(logs, r => r.Path == "/api/issues" && r.Method == "GET");
        var write = Assert.Single(logs, r => r.Path == "/api/issues" && r.Method == "POST");
        Assert.Equal(201, write.StatusCode);
        Assert.Equal("staff-a@example.com", write.UserEmail);
        Assert.Equal("Staff", write.UserRole);

        var refused = Assert.Single(logs, r => r.Path == "/api/users");
        Assert.Equal(403, refused.StatusCode);
        Assert.NotNull(refused.UserId);

        var login = Assert.Single(logs, r => r.Path == "/api/auth/login");
        Assert.Equal(400, login.StatusCode);
        Assert.Equal("someone@example.com", login.UserEmail);   // who was being tried
        Assert.Null(login.UserId);
    }

    [Fact]
    public async Task Every_response_says_how_long_the_server_took()
    {
        var response = await (await As("staff-a")).GetAsync("/api/issues");
        var timing = response.Headers.GetValues("Server-Timing").Single();
        Assert.StartsWith("app;dur=", timing);
    }

    // ───────────── The SuperAdmin overview ─────────────

    [Fact]
    public async Task Only_a_SuperAdmin_can_open_the_overview_the_request_log_and_the_audit_log()
    {
        foreach (var user in new[] { "director", "manager-a", "staff-a", "it-a" })
        {
            var client = await As(user);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/overview")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/request-logs")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/audit")).StatusCode);
        }

        var admin = await As("admin");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/request-logs")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/audit")).StatusCode);
    }

    [Fact]
    public async Task The_overview_pulls_people_requests_issues_and_activity_together()
    {
        var staff = await As("staff-a", "203.0.113.50");
        await staff.PostAsync("/api/issues", ReportForm("เรื่องสำหรับภาพรวม"));
        await staff.GetAsync("/api/users"); // a refusal for the security list
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await WaitUntilAsync(() => db.RequestLogs.Any(r => r.IpAddress == "203.0.113.50" && r.StatusCode == 403));
        }

        var admin = await As("admin");
        var overview = await Json(await admin.GetAsync("/api/admin/overview?days=7"));

        Assert.True(overview.GetProperty("users").GetProperty("total").GetInt32() >= 10);
        Assert.True(overview.GetProperty("requests").GetProperty("forbidden").GetInt32() >= 1);
        Assert.True(overview.GetProperty("issues").GetProperty("open").GetInt32() >= 1);
        Assert.Contains(overview.GetProperty("topIps").EnumerateArray(), i => i.GetProperty("label").GetString() == "203.0.113.50");
        Assert.Contains(overview.GetProperty("securityEvents").EnumerateArray(), e => e.GetProperty("statusCode").GetInt32() == 403);
        Assert.Contains(overview.GetProperty("activity").EnumerateArray(), a => a.GetProperty("entityName").GetString() == "Issue");
    }

    [Fact]
    public async Task The_audit_log_can_be_searched_and_the_request_log_filtered()
    {
        var marker = $"192.0.2.{Random.Shared.Next(10, 250)}";
        var staff = await As("staff-a", marker);
        await staff.PostAsync("/api/issues", ReportForm("ค้นหาใน audit"));
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await WaitUntilAsync(() => db.RequestLogs.Any(r => r.IpAddress == marker));
        }

        var admin = await As("admin");
        var audit = await Json(await admin.GetAsync($"/api/audit?entity=Issue&ip={marker}"));
        Assert.Equal(1, audit.GetProperty("total").GetInt32());
        Assert.Equal("Create", audit.GetProperty("items")[0].GetProperty("action").GetString());

        var requests = await Json(await admin.GetAsync($"/api/admin/request-logs?ip={marker}&method=post"));
        Assert.Equal(1, requests.GetProperty("total").GetInt32());
        var none = await Json(await admin.GetAsync($"/api/admin/request-logs?ip={marker}&minStatus=400"));
        Assert.Equal(0, none.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task The_overview_lists_who_is_online_right_now()
    {
        var token = await _factory.TokenAsync("staff-a");
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/kpi"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
        await connection.StartAsync();

        var admin = await As("admin");
        var overview = await Json(await admin.GetAsync("/api/admin/overview"));
        Assert.Contains(overview.GetProperty("online").EnumerateArray(),
            o => o.GetProperty("role").GetString() == "Staff" && o.GetProperty("name").GetString()!.Contains("staff-a"));

        await connection.StopAsync();
    }

    [Fact]
    public async Task IT_in_the_queue_group_hears_about_a_new_report_live()
    {
        var token = await _factory.TokenAsync("it-b");
        var received = new List<JsonElement>();
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/kpi"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
        connection.On<JsonElement>("Notification", n => { lock (received) received.Add(n); });
        await connection.StartAsync();

        await (await As("staff-a")).PostAsync("/api/issues", ReportForm("ถึงทีม IT แบบสด ๆ"));

        await WaitUntilAsync(() => { lock (received) return received.Count >= 1; });
        lock (received)
            Assert.Contains(received, n => n.GetProperty("title").GetString() == "แจ้งปัญหาใหม่");
    }

    private async Task<int> CountIssuesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Issues.CountAsync();
    }
}
